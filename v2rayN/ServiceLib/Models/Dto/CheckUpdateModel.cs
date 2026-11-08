namespace ServiceLib.Models.Dto;

public partial class CheckUpdateModel : ReactiveObject
{
    public bool? IsSelected { get; set; }
    public bool IsCheckPreRelease { get; set; }
    public bool ShowCheckPreRelease { get; set; }
    public ECoreType? CoreType { get; set; }
    [Reactive] public partial string? Remarks { get; set; }
    public string? FileName { get; set; }
    public bool? IsFinished { get; set; }
    public bool IsGeoFile { get; set; }
    // PattN: the row of the Psiphon server list of the Aether core (design 3.6), which no core reads while it runs
    public bool IsPsiphonServers { get; set; }
    // PattN: the name the Psiphon server list's row is stored by in SelectedCoreTypes
    public const string PsiphonServersStorageName = "PsiphonServers";
    // PattN: the Psiphon server list's row has no core type either, but a name of its own
    public string CoreTypeForStorage => IsPsiphonServers ? PsiphonServersStorageName
        : IsGeoFile ? "GeoFiles" : (CoreType?.ToString() ?? "");
    // PattN: the Aether core's row reads "aether-core"; it is stored as "aether". The Psiphon server list's row reads
    // "psiphon_servers"; it is stored as "PsiphonServers"
    public string CoreTypeDisplay => CoreType == ECoreType.v2rayN ? Global.AppName
        : CoreType == ECoreType.aether ? "aether-core"
        : IsPsiphonServers ? UpdateService.PsiphonServersName
        : CoreTypeForStorage;
}
