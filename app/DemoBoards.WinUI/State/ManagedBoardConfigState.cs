namespace DemoBoards_WinUI.State;

public sealed record ManagedBoardConfigState(
    string RawUiJson,
    string RawMetadataJson,
    string RawLayoutJson,
    string RawBoardJson);