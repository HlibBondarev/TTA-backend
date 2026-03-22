using System.ComponentModel;

namespace TTA.DataAccess.Enums;

public enum AppRole
{
    [Description("Complete CRUD access to the target scope, including staff management")]
    FullControl,

    [Description("Can create and edit data (matches, TTA), but cannot delete entities or manage permissions")]
    Editor,

    [Description("Read-only access to matches, statistics, and rosters")]
    Viewer
}