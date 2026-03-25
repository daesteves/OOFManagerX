using Moq;
using Microsoft.Extensions.Logging;
using OOFManagerX.Core.Interfaces;
using OOFManagerX.Core.Models;
using OOFManagerX.Core.Services;

namespace OOFManagerX.Core.Tests;

public class ScheduleServiceTests
{
    private readonly Mock<ISettingsService> _mockSettingsService;
    private readonly Mock<ILogger<ScheduleService>> _mockLogger;
    private readonly ScheduleService _scheduleService;
    private readonly OOFSchedule _defaultSchedule;

    public ScheduleServiceTests()
    {
        _mockSettingsService = new Mock<ISettingsService>();
        _mockLogger = new Mock<ILogger<ScheduleService>>();
        
        // Create a default schedule: Mon-Fri 9-17, Sat-Sun off
        var workingDays = new List<WorkingDay>
        {
            new(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(17, 0), false),
            new(DayOfWeek.Tuesday, new TimeOnly(9, 0), new TimeOnly(17, 0), false),
            new(DayOfWeek.Wednesday, new TimeOnly(9, 0), new TimeOnly(17, 0), false),
            new(DayOfWeek.Thursday, new TimeOnly(9, 0), new TimeOnly(17, 0), false),
            new(DayOfWeek.Friday, new TimeOnly(9, 0), new TimeOnly(17, 0), false),
            new(DayOfWeek.Saturday, new TimeOnly(9, 0), new TimeOnly(17, 0), true),
            new(DayOfWeek.Sunday, new TimeOnly(9, 0), new TimeOnly(17, 0), true),
        };
        
        _defaultSchedule = new OOFSchedule
        {
            WorkingDays = workingDays.AsReadOnly(),
            PrimaryMessage = new OOFMessage("I'm away", "Thank you for your email"),
            ExtendedMessage = new OOFMessage("On vacation", "On vacation until..."),
            ExternalAudience = ExternalAudienceScope.All
        };
        
        _mockSettingsService.Setup(s => s.LoadScheduleAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_defaultSchedule);
        
        _scheduleService = new ScheduleService(_mockSettingsService.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task InitializeAsync_LoadsScheduleFromSettings()
    {
        // Act
        await _scheduleService.InitializeAsync();
        var schedule = _scheduleService.GetSchedule();

        // Assert
        Assert.Equal(7, schedule.WorkingDays.Count);
        _mockSettingsService.Verify(s => s.LoadScheduleAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(DayOfWeek.Monday, 8, 0, true)]   // Before work hours - OOF should be active
    [InlineData(DayOfWeek.Monday, 9, 0, false)]  // At start of work - OOF should be inactive
    [InlineData(DayOfWeek.Monday, 12, 0, false)] // During work hours - OOF should be inactive
    [InlineData(DayOfWeek.Monday, 17, 0, false)] // At end of work (inclusive) - OOF should be inactive
    [InlineData(DayOfWeek.Monday, 17, 1, true)]  // After end of work - OOF should be active
    [InlineData(DayOfWeek.Monday, 18, 0, true)]  // After work hours - OOF should be active
    [InlineData(DayOfWeek.Saturday, 12, 0, true)] // Weekend - OOF should be active
    [InlineData(DayOfWeek.Sunday, 10, 0, true)]  // Weekend - OOF should be active
    public async Task ShouldOOFBeActive_ReturnsCorrectState(DayOfWeek day, int hour, int minute, bool expectedActive)
    {
        // Arrange
        await _scheduleService.InitializeAsync();
        
        // Find a date that falls on the specified day of week
        var testDate = GetNextDateForDayOfWeek(day);
        var dateTime = testDate.Add(new TimeSpan(hour, minute, 0));

        // Act
        var result = _scheduleService.ShouldOOFBeActive(dateTime);

        // Assert
        Assert.Equal(expectedActive, result);
    }

    [Fact]
    public async Task ShouldOOFBeActive_WithExtendedOOF_ReturnsTrue()
    {
        // Arrange - use fresh mock to avoid constructor setup interference
        var freshMock = new Mock<ISettingsService>();
        var now = DateTime.Now;
        var scheduleWithExtended = _defaultSchedule with
        {
            IsExtendedOOFActive = true,
            ExtendedOOFEndDate = now.AddDays(7) // 7 days in the future
        };
        
        freshMock.Setup(s => s.LoadScheduleAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(scheduleWithExtended);
        
        var service = new ScheduleService(freshMock.Object, _mockLogger.Object);
        await service.InitializeAsync();
        
        // Verify schedule was loaded correctly
        var loadedSchedule = service.GetSchedule();
        Assert.True(loadedSchedule.IsExtendedOOFActive, "Schedule should have IsExtendedOOFActive=true");
        Assert.NotNull(loadedSchedule.ExtendedOOFEndDate);
        
        // Act - check at current time (which is before extended OOF end date)
        var result = service.ShouldOOFBeActive(now);

        // Assert - should be active because extended OOF is on and hasn't expired
        Assert.True(result);
    }

    [Fact]
    public async Task ShouldOOFBeActive_WithExpiredExtendedOOF_FallsBackToSchedule()
    {
        // Arrange
        var scheduleWithExpiredExtended = _defaultSchedule with
        {
            IsExtendedOOFActive = true,
            ExtendedOOFEndDate = DateTime.Now.AddDays(-1) // Expired yesterday
        };
        
        _mockSettingsService.Setup(s => s.LoadScheduleAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(scheduleWithExpiredExtended);
        
        var service = new ScheduleService(_mockSettingsService.Object, _mockLogger.Object);
        await service.InitializeAsync();
        
        // Act - check during working hours on a weekday
        var monday = GetNextDateForDayOfWeek(DayOfWeek.Monday).Add(new TimeSpan(12, 0, 0));
        var result = service.ShouldOOFBeActive(monday);

        // Assert - should be inactive because extended OOF expired, and it's working hours
        Assert.False(result);
    }

    [Fact]
    public async Task GetCurrentMessage_ReturnsPrimaryMessage_WhenExtendedNotActive()
    {
        // Arrange
        await _scheduleService.InitializeAsync();

        // Act
        var message = _scheduleService.GetCurrentMessage();

        // Assert
        Assert.Equal("I'm away", message.InternalMessage);
    }

    [Fact]
    public async Task GetCurrentMessage_ReturnsExtendedMessage_WhenExtendedActive()
    {
        // Arrange
        var scheduleWithExtended = _defaultSchedule with
        {
            IsExtendedOOFActive = true,
            ExtendedOOFEndDate = DateTime.Now.AddDays(7)
        };
        
        _mockSettingsService.Setup(s => s.LoadScheduleAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(scheduleWithExtended);
        
        var service = new ScheduleService(_mockSettingsService.Object, _mockLogger.Object);
        await service.InitializeAsync();

        // Act
        var message = service.GetCurrentMessage();

        // Assert
        Assert.Equal("On vacation", message.InternalMessage);
    }

    [Fact]
    public async Task GetOOFScheduleWindow_DuringWorkHours_ReturnsNextOOFPeriod()
    {
        // Arrange
        await _scheduleService.InitializeAsync();
        
        // Monday at noon (during working hours, 9-17)
        var monday = GetNextDateForDayOfWeek(DayOfWeek.Monday).Add(new TimeSpan(12, 0, 0));

        // Act
        var result = _scheduleService.GetOOFScheduleWindow(monday);

        // Assert
        Assert.NotNull(result);
        var (start, end, _) = result.Value;
        // Start should be today's work end (17:00)
        Assert.Equal(monday.Date.Add(new TimeSpan(17, 0, 0)), start);
        // End should be next work start (Tuesday 9:00)
        Assert.True(end > start);
        Assert.Equal(new TimeSpan(9, 0, 0), end.TimeOfDay);
    }

    [Fact]
    public async Task GetOOFScheduleWindow_AfterWorkHours_ReturnsCurrentOOFPeriod()
    {
        // Arrange
        await _scheduleService.InitializeAsync();
        
        // Monday at 19:00 (after work)
        var monday = GetNextDateForDayOfWeek(DayOfWeek.Monday).Add(new TimeSpan(19, 0, 0));

        // Act
        var result = _scheduleService.GetOOFScheduleWindow(monday);

        // Assert
        Assert.NotNull(result);
        var (start, end, _) = result.Value;
        // Start should be today's work end (17:00) — when OOF period began
        Assert.Equal(monday.Date.Add(new TimeSpan(17, 0, 0)), start);
        // End should be Tuesday 9:00
        Assert.Equal(monday.Date.AddDays(1).Add(new TimeSpan(9, 0, 0)), end);
    }

    [Fact]
    public async Task GetOOFScheduleWindow_BeforeWorkHours_ReturnsCurrentOOFPeriod()
    {
        // Arrange
        await _scheduleService.InitializeAsync();
        
        // Tuesday at 7:00 (before work)
        var tuesday = GetNextDateForDayOfWeek(DayOfWeek.Tuesday).Add(new TimeSpan(7, 0, 0));

        // Act
        var result = _scheduleService.GetOOFScheduleWindow(tuesday);

        // Assert
        Assert.NotNull(result);
        var (start, end, _) = result.Value;
        // Start should be Monday's work end (17:00) — when OOF period began
        Assert.Equal(tuesday.Date.AddDays(-1).Add(new TimeSpan(17, 0, 0)), start);
        // End should be Tuesday 9:00 (work starts)
        Assert.Equal(tuesday.Date.Add(new TimeSpan(9, 0, 0)), end);
    }

    [Fact]
    public async Task GetOOFScheduleWindow_OnWeekend_SpansFridayToMonday()
    {
        // Arrange
        await _scheduleService.InitializeAsync();
        
        // Saturday at 14:00
        var saturday = GetNextDateForDayOfWeek(DayOfWeek.Saturday).Add(new TimeSpan(14, 0, 0));

        // Act
        var result = _scheduleService.GetOOFScheduleWindow(saturday);

        // Assert
        Assert.NotNull(result);
        var (start, end, _) = result.Value;
        // Start should be Friday's work end (17:00)
        Assert.Equal(saturday.Date.AddDays(-1).Add(new TimeSpan(17, 0, 0)), start);
        // End should be Monday 9:00
        Assert.Equal(saturday.Date.AddDays(2).Add(new TimeSpan(9, 0, 0)), end);
    }

    [Fact]
    public async Task GetOOFScheduleWindow_FridayDuringWork_SchedulesWeekend()
    {
        // Arrange
        await _scheduleService.InitializeAsync();
        
        // Friday at 10:00 (during work)
        var friday = GetNextDateForDayOfWeek(DayOfWeek.Friday).Add(new TimeSpan(10, 0, 0));

        // Act
        var result = _scheduleService.GetOOFScheduleWindow(friday);

        // Assert
        Assert.NotNull(result);
        var (start, end, _) = result.Value;
        // Start should be Friday 17:00
        Assert.Equal(friday.Date.Add(new TimeSpan(17, 0, 0)), start);
        // End should be Monday 9:00 (skips weekend)
        Assert.Equal(DayOfWeek.Monday, end.DayOfWeek);
        Assert.Equal(new TimeSpan(9, 0, 0), end.TimeOfDay);
    }

    [Fact]
    public async Task GetOOFScheduleWindow_SundayEvening_EndsMonday()
    {
        // Arrange
        await _scheduleService.InitializeAsync();
        
        // Sunday at 22:00
        var sunday = GetNextDateForDayOfWeek(DayOfWeek.Sunday).Add(new TimeSpan(22, 0, 0));

        // Act
        var result = _scheduleService.GetOOFScheduleWindow(sunday);

        // Assert
        Assert.NotNull(result);
        var (start, end, _) = result.Value;
        // End should be Monday 9:00
        Assert.Equal(sunday.Date.AddDays(1).Add(new TimeSpan(9, 0, 0)), end);
    }

    [Fact]
    public async Task SaveScheduleAsync_PersistsToSettingsService()
    {
        // Arrange
        await _scheduleService.InitializeAsync();
        var newSchedule = _defaultSchedule with
        {
            PrimaryMessage = new OOFMessage("New internal", "New external")
        };

        // Act
        await _scheduleService.SaveScheduleAsync(newSchedule);

        // Assert
        _mockSettingsService.Verify(s => s.SaveScheduleAsync(newSchedule, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static DateTime GetNextDateForDayOfWeek(DayOfWeek dayOfWeek)
    {
        var today = DateTime.Today;
        var daysUntilTarget = ((int)dayOfWeek - (int)today.DayOfWeek + 7) % 7;
        if (daysUntilTarget == 0) daysUntilTarget = 7; // If today is the target day, get next week
        return today.AddDays(daysUntilTarget);
    }
}
