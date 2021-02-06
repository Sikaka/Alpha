using System.Windows.Forms;
using ExileCore.Shared.Attributes;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;
using System.IO;

namespace Alpha
{
	class AlphaSettings : ISettings
	{
		public ToggleNode Enable { get; set; } = new ToggleNode(false);
		public ToggleNode IsFollowEnabled { get; set; } = new ToggleNode(false);
		[Menu("Toggle Follower")] public HotkeyNode ToggleFollower { get; set; } = Keys.PageUp;
		[Menu("Min Path Distance")] public RangeNode<int> PathfindingNodeDistance { get; set; } = new RangeNode<int>(200, 10, 1000);
		[Menu("Move CMD Frequency")]public RangeNode<int> BotInputFrequency { get; set; } = new RangeNode<int>(50, 10, 250);
		[Menu("Stop Path Distance")] public RangeNode<int> ClearPathDistance { get; set; } = new RangeNode<int>(500, 100, 5000);
		[Menu("Follow Target Name")] public TextNode LeaderName { get; set; } = new TextNode("");
		[Menu("Movement Key")] public HotkeyNode MovementKey { get; set; } = Keys.T;

	}
}