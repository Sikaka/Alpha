using System;
using System.Runtime.InteropServices;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Cache;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using System.Collections.Generic;
using System.Drawing;
using Map = ExileCore.PoEMemory.Elements.Map;
using EpPathFinding.cs;
using System.Linq;
using ExileCore.Shared.Helpers;
using System.IO;
using System.Threading.Tasks;

namespace Alpha
{
	/// <summary>
	/// All work is shamelessly leached and cobbled together. 
	///		Follower: 13413j1j13j5315n13
	///		Terrain: mm3141
	///		Pathfinding: juhgiyo
	///	I'm just linking things together and doing silly experiments. 
	/// </summary>
	internal class AlphaCore : BaseSettingsPlugin<AlphaSettings>
	{
		private int _numRows, _numCols;
		private StaticGrid _mapGrid;
		private Camera Camera => GameController.Game.IngameState.Camera;		
		private List<GridPos> _path = null;
		private Dictionary<string, Vector2> _areaTransitions = new Dictionary<string, Vector2>();


		private Random random = new Random();


		private List<Vector2> _targetPositions = new List<Vector2>();
		private Vector2 _lastTargetPosition;
		private Entity _followTarget;

		private DateTime _nextBotAction = DateTime.Now;

		public AlphaCore()
		{
			Name = "Alpha";
		}

		public override bool Initialise()
		{
			Input.RegisterKey(Settings.MovementKey.Value);

			Input.RegisterKey(Settings.ToggleFollower.Value);
			Settings.ToggleFollower.OnValueChanged += () => { Input.RegisterKey(Settings.ToggleFollower.Value); };

			Input.RegisterKey(Settings.FindPath.Value);
			Settings.FindPath.OnValueChanged += () => { Input.RegisterKey(Settings.FindPath.Value); };
			return base.Initialise();
		}


		/// <summary>
		/// Clears all pathfinding values. Used on area transitions primarily.
		/// </summary>
		private void ResetPathing()
		{
			_targetPositions = new List<Vector2>();
			_followTarget = null;
			_path = null;
			_lastTargetPosition = Vector2.Zero;
		}

		public override void AreaChange(AreaInstance area)
		{
			ResetPathing();
			_areaTransitions = new Dictionary<string, Vector2>();
			var terrain = GameController.IngameState.Data.Terrain;
			var terrainBytes = GameController.Memory.ReadBytes(terrain.LayerMelee.First, terrain.LayerMelee.Size);
			_numCols = (int)(terrain.NumCols - 1) * 23;
			_numRows = (int)(terrain.NumRows - 1) * 23;
			if ((_numCols & 1) > 0)
				_numCols++;

			_mapGrid = new StaticGrid(_numCols, _numRows);
			int dataIndex = 0;
			for (int y = 0; y < _numRows; y++)
			{
				for (int x = 0; x < _numCols; x += 2)
				{
					var b = terrainBytes[dataIndex + (x >> 1)];
					_mapGrid.SetWalkableAt(x, y, (b & 0xf) > 0);
					_mapGrid.SetWalkableAt(x+1, y, (b >> 4) > 0);
				}
				dataIndex += terrain.BytesPerRow;
			}
			//GeneratePNG();
		}


		public void GeneratePNG()
		{
			using (var img = new Bitmap(_numRows, _numCols))
			{
				for (int x = 0; x < _numRows; x++)
					for (int y = 0; y < _numCols; y++)
						img.SetPixel(x, y, _mapGrid.IsWalkableAt(x, y) ? System.Drawing.Color.White : System.Drawing.Color.Black);

				foreach (var pos in _areaTransitions.Values)
				{
					for (var x = (int)pos.X - 2; x < pos.X + 2; x++)
						for (var y = (int)pos.Y - 2; y < pos.Y + 2; y++)
							img.SetPixel(x, y, System.Drawing.Color.Red);
				}
				for (var x = (int)GameController.Player.GridPos.X - 2; x < GameController.Player.GridPos.X + 2; x++)
					for (var y = (int)GameController.Player.GridPos.Y - 2; y < GameController.Player.GridPos.Y + 2; y++)
						img.SetPixel(x, y, System.Drawing.Color.Blue);

				img.Save("output.png");
			}
		}

		public override Job Tick()
		{
			if (Settings.ToggleFollower.PressedOnce())			
				Settings.IsFollowEnabled.SetValueNoEvent(!Settings.IsFollowEnabled.Value);

			if (!Settings.IsFollowEnabled.Value)
				return null;


			//Cache the current follow target (if present)
			_followTarget = GetFollowingTarget();

			if(_followTarget != null)
			{
				if(GameController.Player.GridPos.Distance(_followTarget.GridPos) >= Settings.ClearPathDistance.Value)
				{

					if (_targetPositions.Count > 0 && _targetPositions.Last().Distance(_followTarget.GridPos) > Settings.PathfindingNodeDistance)
						_targetPositions.Add(_followTarget.GridPos);
					else if(_targetPositions.Count == 0)
						_targetPositions.Add(_followTarget.GridPos);
				}

				//If we're already close to the target, clear our path. 
				if(GameController.Player.GridPos.Distance(_followTarget.GridPos) <= Settings.ClearPathDistance)				
					_targetPositions = new List<Vector2>();
				
				_lastTargetPosition = _followTarget.GridPos;
			}

			//Check last movement input time
			if (DateTime.Now > _nextBotAction)
			{
				//Check if any paths remain. 
				while (_targetPositions.Count > 1)
				{
					//Skip any paths that are too close to us. 
					if (GameController.Player.GridPos.Distance(_targetPositions[0]) < Settings.PathfindingNodeDistance)
						_targetPositions.RemoveAt(0);
					else
						break;
				}

				//Check if we have a path remaining
				if (_targetPositions.Count > 0)
				{
					_nextBotAction = DateTime.Now.AddMilliseconds(Settings.BotInputFrequency / 2 + random.Next(Settings.BotInputFrequency / 2));
					var next = _targetPositions[0];
					var cameraPos = GridToValidScreenPos(next);
					Mouse.SetCursorPosHuman2(cameraPos);
					System.Threading.Thread.Sleep(random.Next(25) + 30);
					Input.KeyDown(Settings.MovementKey);
					System.Threading.Thread.Sleep(random.Next(25) + 30);
					Input.KeyUp(Settings.MovementKey);

					if (GameController.Player.GridPos.Distance(next) < Settings.PathfindingNodeDistance)
						_targetPositions.RemoveAt(0);
				}
				else if (_followTarget == null &&
					_areaTransitions.Count > 0 &&
					_lastTargetPosition != Vector2.Zero)
				{
					_nextBotAction = DateTime.Now.AddMilliseconds(Settings.BotInputFrequency * 5 + random.Next(Settings.BotInputFrequency * 5));
					//Check if we're too far away from the transition. If os move towards last known first. 
					if (_lastTargetPosition.Distance(GameController.Player.GridPos) > Settings.ClearPathDistance)
					{
						Mouse.SetCursorPosHuman2(GridToValidScreenPos(_lastTargetPosition));
						System.Threading.Thread.Sleep(random.Next(25) + 30);
						Input.KeyDown(Settings.MovementKey);
						System.Threading.Thread.Sleep(random.Next(25) + 30);
						Input.KeyUp(Settings.MovementKey);
					}
					else
					{
						var transitionTarget = _areaTransitions.Values.OrderBy(I => I.Distance(GameController.Player.GridPos)).FirstOrDefault();
						if (transitionTarget.Distance(_lastTargetPosition) <= Settings.ClearPathDistance)
						{
							Input.KeyUp(Settings.MovementKey);
							Mouse.SetCursorPosAndLeftClickHuman(GridToValidScreenPos(transitionTarget), 100);
							_nextBotAction = DateTime.Now.AddSeconds(1);
						}
					}
				}
			}

			return null;
		}

		private Entity GetFollowingTarget()
		{
			var leaderName = Settings.LeaderName.Value.ToLower();
			try
			{
				return GameController.Entities
					.Where(x => x.Type == ExileCore.Shared.Enums.EntityType.Player)
					.FirstOrDefault(x => x.GetComponent<Player>().PlayerName.ToLower() == leaderName);
			}
			// Sometimes we can get "Collection was modified; enumeration operation may not execute" exception
			catch
			{
				return null;
			}
		}

		public override void EntityAdded(Entity entity)
		{
			if (!string.IsNullOrEmpty(entity.RenderName))
				switch (entity.Type)
				{
					//TODO: Handle doors and similar obstructions to movement/pathfinding

					//TODO: Handle waypoint (initial claim as well as using to teleport somewhere)

					//Handle clickable teleporters
					case ExileCore.Shared.Enums.EntityType.AreaTransition:
					case ExileCore.Shared.Enums.EntityType.Portal:
					case ExileCore.Shared.Enums.EntityType.TownPortal:
						if (!_areaTransitions.ContainsKey(entity.RenderName))
						{
							_areaTransitions.Add(entity.RenderName, entity.GridPos);
							
						}
						break;
				}
			base.EntityAdded(entity);
		}


		public override void Render()
		{
			if (_path != null && _path.Count > 1)
				for (var i = 1; i < _path.Count; i++)
				{
					var start = GridToScreen(new Vector2(_path[i - 1].x, _path[i - 1].y));
					var end = GridToScreen(new Vector2(_path[i].x, _path[i].y));
					Graphics.DrawLine(start, end, 2, SharpDX.Color.Pink);
				}
			Graphics.DrawText($"Follow Enabled: {Settings.IsFollowEnabled.Value}", new Vector2(500, 20));
			Graphics.DrawText($"Pathing Waypoints: {_targetPositions.Count}", new Vector2(500, 40));
			var counter = 0;
			foreach (var transition in _areaTransitions)
			{
				counter++;
				Graphics.DrawText($"{transition.Key} at { transition.Value.X} { transition.Value.Y}", new Vector2(100, 20 + counter * 20));
			}
		}

		private Vector2 GridToScreen(Vector2 gridPos)
		{
			var worldPos = PoeMapExtension.GridToWorld(gridPos);
			return Camera.WorldToScreen(new Vector3(worldPos.X, worldPos.Y, 0));
		}

		private Vector2 GridToValidScreenPos(Vector2 gridPos)
		{
			var worldPos = PoeMapExtension.GridToWorld(gridPos);
			var cameraPos = GridToScreen(gridPos);

			//Clamp to inner section of screen. 
			var window = GameController.Window.GetWindowRectangle();

			//We want to clamp to the center of screen.
			var bounds = new Vector2(window.Width / 5, window.Height / 5);
			cameraPos = new Vector2(MathUtil.Clamp(cameraPos.X, bounds.X, window.Width- bounds.X),
				MathUtil.Clamp(cameraPos.Y, bounds.Y, window.Height - bounds.Y));

			return cameraPos;

		}
	}
}