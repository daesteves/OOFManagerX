using System.Globalization;

namespace OOFManagerX.Core.Tests;

public class LocalizedStringsTests
{
    [Fact]
    public void GetDayName_ReturnsLocalizedDayName()
    {
        // Arrange
        var expectedMonday = CultureInfo.CurrentCulture.DateTimeFormat.GetDayName(DayOfWeek.Monday);

        // Act
        var result = LocalizedStrings.GetDayName(DayOfWeek.Monday);

        // Assert
        Assert.Equal(expectedMonday, result);
    }

    [Fact]
    public void GetShortDayName_ReturnsAbbreviatedDayName()
    {
        // Arrange
        var expectedMon = CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedDayName(DayOfWeek.Monday);

        // Act
        var result = LocalizedStrings.GetShortDayName(DayOfWeek.Monday);

        // Assert
        Assert.Equal(expectedMon, result);
    }

    [Fact]
    public void FormatTime_UsesCurrentCulture()
    {
        // Arrange
        var time = new DateTime(2026, 1, 15, 14, 30, 0);
        var expected = time.ToString("t", CultureInfo.CurrentCulture);

        // Act
        var result = LocalizedStrings.FormatTime(time);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void FormatDate_UsesCurrentCulture()
    {
        // Arrange
        var date = new DateTime(2026, 1, 15);
        var expected = date.ToString("d", CultureInfo.CurrentCulture);

        // Act
        var result = LocalizedStrings.FormatDate(date);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void FormatDateTime_UsesCurrentCulture()
    {
        // Arrange
        var dateTime = new DateTime(2026, 1, 15, 14, 30, 0);
        var expected = dateTime.ToString("g", CultureInfo.CurrentCulture);

        // Act
        var result = LocalizedStrings.FormatDateTime(dateTime);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void FormatRelativeTime_ReturnsNow_ForCurrentTime()
    {
        // Arrange
        var now = DateTime.Now;

        // Act
        var result = LocalizedStrings.FormatRelativeTime(now);

        // Assert
        Assert.Equal("now", result);
    }

    [Fact]
    public void FormatRelativeTime_ReturnsFutureMinutes()
    {
        // Arrange
        var future = DateTime.Now.AddMinutes(30);

        // Act
        var result = LocalizedStrings.FormatRelativeTime(future);

        // Assert - allow for slight timing differences
        Assert.Contains("min", result);
        Assert.StartsWith("in ", result);
    }

    [Fact]
    public void FormatRelativeTime_ReturnsFutureHours()
    {
        // Arrange
        var future = DateTime.Now.AddHours(3);

        // Act
        var result = LocalizedStrings.FormatRelativeTime(future);

        // Assert
        Assert.Contains("hr", result);
        Assert.StartsWith("in ", result);
    }

    [Fact]
    public void FormatRelativeTime_ReturnsFutureDays()
    {
        // Arrange
        var future = DateTime.Now.AddDays(5);

        // Act
        var result = LocalizedStrings.FormatRelativeTime(future);

        // Assert
        Assert.Contains("days", result);
        Assert.StartsWith("in ", result);
    }

    [Fact]
    public void FormatRelativeTime_ReturnsPastMinutes()
    {
        // Arrange
        var past = DateTime.Now.AddMinutes(-15);

        // Act
        var result = LocalizedStrings.FormatRelativeTime(past);

        // Assert
        Assert.Contains("15", result);
        Assert.Contains("min", result);
        Assert.Contains("ago", result);
    }

    [Fact]
    public void FormatOOFScheduled_ShowsActiveUntil_WhenCurrentlyActive()
    {
        // Arrange
        var start = DateTime.Now.AddHours(-1);
        var end = DateTime.Now.AddHours(5);

        // Act
        var result = LocalizedStrings.FormatOOFScheduled(start, end);

        // Assert
        Assert.Contains("active until", result);
    }

    [Fact]
    public void FormatOOFScheduled_ShowsScheduled_WhenFuture()
    {
        // Arrange
        var start = DateTime.Now.AddHours(2);
        var end = DateTime.Now.AddHours(10);

        // Act
        var result = LocalizedStrings.FormatOOFScheduled(start, end);

        // Assert
        Assert.Contains("scheduled", result);
        Assert.Contains("→", result);
    }

    [Fact]
    public void StaticStrings_AreNotEmpty()
    {
        // Assert that all static string properties return non-empty values
        Assert.False(string.IsNullOrEmpty(LocalizedStrings.NotSignedIn));
        Assert.False(string.IsNullOrEmpty(LocalizedStrings.Ready));
        Assert.False(string.IsNullOrEmpty(LocalizedStrings.Monitoring));
        Assert.False(string.IsNullOrEmpty(LocalizedStrings.Paused));
        Assert.False(string.IsNullOrEmpty(LocalizedStrings.OOFEnabled));
        Assert.False(string.IsNullOrEmpty(LocalizedStrings.OOFDisabled));
        Assert.False(string.IsNullOrEmpty(LocalizedStrings.OpenOOFManagerX));
        Assert.False(string.IsNullOrEmpty(LocalizedStrings.Exit));
    }
}
