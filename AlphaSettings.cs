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
			CalculatePathFrequency = new RangeNode<int>(3000, 1000, 10000);
		}

		public ToggleNode Enable { get; set; } = new ToggleNode(false);
		public ToggleNode IsFollowEnabled { get; set; } = new ToggleNode(false);
		[Menu("Toggle Follower")] public HotkeyNode ToggleFollower { get; set; } = Keys.PageUp;

		[Menu("Trigger Pathfinding")] public HotkeyNode FindPath { get; set; } = Keys.PageDown;

		[Menu("Min Distance")]
		public RangeNode<int> PathfindingNodeDistance { get; set; }

		[Menu("Movement Key")] public HotkeyNode MovementKey { get; set; } = Keys.T;

		[Menu("Calculate Path Frequency")]
		public RangeNode<int> CalculatePathFrequency { get; set; }


		[Menu("Follow Target")]
		public TextNode FollowTarget { get; set; }

	}
}