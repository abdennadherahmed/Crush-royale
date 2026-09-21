using System.Runtime.CompilerServices;

// The editor screenshot tool builds every screen the way UIRoot does (UIScreen.Setup) and feeds it an offline
// profile, and the test assembly does the same: both need the internals the game only exposes to itself.
[assembly: InternalsVisibleTo("CrushRoyale.Editor")]
[assembly: InternalsVisibleTo("CrushRoyale.Tests")]
