using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace OOFManagerX.App.Converters;

public static class BoolConverters
{
    public static readonly IValueConverter ToOpacity =
        new FuncValueConverter<bool, double>(b => b ? 1.0 : 0.4);

    public static readonly IValueConverter ToChevron =
        new FuncValueConverter<bool, string>(b => b ? "▾" : "▸");

    public static readonly IValueConverter ToSyncHint =
        new FuncValueConverter<bool, string>(b => b
            ? "Working hours are imported from Outlook"
            : "Edit your schedule manually below");
}
