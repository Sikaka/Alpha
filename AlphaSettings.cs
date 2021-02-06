using System.Windows.Forms;
using ExileCore.Shared.Attributes;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;
using System.IO;

namespace Alpha
{
	class AlphaSettings : ISettings
	{
		public AlphaSettings()
		{
			PathfindingNodeDistance = new RangeNode<int>(20, 1, 50);
			BotInputFrequency = new RangeNode<int>(50, 10, 250);
			ClearPathDistance = new RangeNode<int>(50, 10, 200);
		}

		public ToggleNode Enable { get; set; } = new ToggleNode(false);
		public ToggleNode IsFollowEnabled { get; set; } = new ToggleNode(false);
		[Menu("Toggle Follower")] public HotkeyNode ToggleFollower { get; set; } = Keys.PageUp;

		[Menu("Trigger Pathfinding")] public HotkeyNode FindPath { get; set; } = Keys.PageDown;

		[Menu("Min Distance")]
		public RangeNode<int> PathfindingNodeDistance { get; set; }

		[Menu("Movement Key")] public HotkeyNode MovementKey { get; set; } = Keys.T;

		[Menu("Calculate Path Frequency")]
		public RangeNode<int> BotInputFrequency { get; set; }
		[Menu("Clear Path Distance")]
		public RangeNode<int> ClearPathDistance { get; set; }

		public TextNode LeaderName { get; set; } = new TextNode("");

	}
}