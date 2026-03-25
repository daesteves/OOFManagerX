using OOFManagerX.Core.Models;

namespace OOFManagerX.Core.Tests;

public class WorkingDayTests
{
    [Fact]
    public void IsCurrentlyWorking_ReturnsTrue_DuringWorkingHours()
    {
        // Arrange
        var workingDay = new WorkingDay(
            DayOfWeek.Monday,
            new TimeOnly(9, 0),
            new TimeOnly(17, 0),
            false);

        // Act & Assert
        Assert.True(workingDay.IsCurrentlyWorking(new TimeOnly(9, 0)));   // Start time
        Assert.True(workingDay.IsCurrentlyWorking(new TimeOnly(12, 0)));  // Middle
        Assert.True(workingDay.IsCurrentlyWorking(new TimeOnly(16, 59))); // Just before end
        Assert.True(workingDay.IsCurrentlyWorking(new TimeOnly(17, 0)));  // End time (inclusive)
    }

    [Fact]
    public void IsCurrentlyWorking_ReturnsFalse_OutsideWorkingHours()
    {
        // Arrange
        var workingDay = new WorkingDay(
            DayOfWeek.Monday,
            new TimeOnly(9, 0),
            new TimeOnly(17, 0),
            false);

        // Act & Assert
        Assert.False(workingDay.IsCurrentlyWorking(new TimeOnly(8, 59)));  // Before start
        Assert.False(workingDay.IsCurrentlyWorking(new TimeOnly(17, 1)));  // After end
        Assert.False(workingDay.IsCurrentlyWorking(new TimeOnly(18, 0)));  // After end
        Assert.False(workingDay.IsCurrentlyWorking(new TimeOnly(0, 0)));   // Midnight
    }

    [Fact]
    public void IsCurrentlyWorking_ReturnsFalse_WhenIsOffWork()
    {
        // Arrange
        var workingDay = new WorkingDay(
            DayOfWeek.Saturday,
            new TimeOnly(9, 0),
            new TimeOnly(17, 0),
            true); // Off work

        // Act & Assert - even during "working hours", should return false
        Assert.False(workingDay.IsCurrentlyWorking(new TimeOnly(12, 0)));
    }

    [Fact]
    public void CreateDefault_CreatesWeekdayAsWorking()
    {
        // Act
        var monday = WorkingDay.CreateDefault(DayOfWeek.Monday);

        // Assert
        Assert.False(monday.IsOffWork);
        Assert.Equal(new TimeOnly(9, 0), monday.StartTime);
        Assert.Equal(new TimeOnly(17, 0), monday.EndTime);
    }

    [Fact]
    public void CreateDefault_CreatesWeekendAsOffWork()
    {
        // Act
        var saturday = WorkingDay.CreateDefault(DayOfWeek.Saturday);
        var sunday = WorkingDay.CreateDefault(DayOfWeek.Sunday);

        // Assert
        Assert.True(saturday.IsOffWork);
        Assert.True(sunday.IsOffWork);
    }
}

public class OOFMessageTests
{
    [Fact]
    public void OOFMessage_DefaultsToEmptyStrings()
    {
        // Act
        var message = new OOFMessage();

        // Assert
        Assert.Equal(string.Empty, message.InternalMessage);
        Assert.Equal(string.Empty, message.ExternalMessage);
    }

    [Fact]
    public void OOFMessage_CanBeCreatedWithValues()
    {
        // Act
        var message = new OOFMessage("Internal", "External");

        // Assert
        Assert.Equal("Internal", message.InternalMessage);
        Assert.Equal("External", message.ExternalMessage);
    }
}

public class OOFScheduleTests
{
    [Fact]
    public void OOFSchedule_DefaultsToStandardWorkWeek()
    {
        // Act
        var schedule = new OOFSchedule();

        // Assert
        Assert.Equal(7, schedule.WorkingDays.Count);
        
        // Check weekdays are working days
        var monday = schedule.WorkingDays.First(d => d.DayOfWeek == DayOfWeek.Monday);
        Assert.False(monday.IsOffWork);
        Assert.Equal(new TimeOnly(9, 0), monday.StartTime);
        Assert.Equal(new TimeOnly(17, 0), monday.EndTime);
        
        // Check weekends are off
        var saturday = schedule.WorkingDays.First(d => d.DayOfWeek == DayOfWeek.Saturday);
        Assert.True(saturday.IsOffWork);
    }

    [Fact]
    public void OOFSchedule_ExtendedOOF_DefaultsToInactive()
    {
        // Act
        var schedule = new OOFSchedule();

        // Assert
        Assert.False(schedule.IsExtendedOOFActive);
        Assert.Null(schedule.ExtendedOOFEndDate);
    }
}

public class UserSettingsTests
{
    [Fact]
    public void UserSettings_MonitoringEnabled_DefaultsToTrue()
    {
        // Act
        var settings = new UserSettings();

        // Assert
        Assert.True(settings.MonitoringEnabled);
    }

    [Fact]
    public void UserSettings_CanBeCreatedWithRecord()
    {
        // Act
        var settings = new UserSettings
        {
            StartMinimized = true,
            MonitoringEnabled = false,
            UserPrincipalName = "user@test.com"
        };

        // Assert
        Assert.True(settings.StartMinimized);
        Assert.False(settings.MonitoringEnabled);
        Assert.Equal("user@test.com", settings.UserPrincipalName);
    }

    [Fact]
    public void UserSettings_WithExpression_CreatesNewInstance()
    {
        // Arrange
        var original = new UserSettings { StartMinimized = true };

        // Act
        var modified = original with { MonitoringEnabled = false };

        // Assert
        Assert.True(original.MonitoringEnabled); // Original unchanged
        Assert.False(modified.MonitoringEnabled); // New instance changed
        Assert.True(modified.StartMinimized); // Other values preserved
    }
}
