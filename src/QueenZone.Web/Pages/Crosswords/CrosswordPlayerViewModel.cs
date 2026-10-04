namespace QueenZone.Web.Pages.Crosswords;

public sealed record CrosswordPlayerViewModel(CrosswordDetailDto Puzzle, Guid? MemberId, bool OfflineShell, bool Preview = false);
