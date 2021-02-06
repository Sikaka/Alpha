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

		private Vector2 _lastKnownTargetPosition;
		private DateTime _lastMovementKey = DateTime.Now;
		private DateTime _lastPathCalculatedAt = DateTime.Now;
		private bool _hasSuccessfullyFollowed = false;
		private Entity _followTarget;

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
			_followTarget = null;
			_path = null;
			_hasSuccessfullyFollowed = false;
			_lastKnownTargetPosition = Vector2.Zero;
			_lastMovementKey = DateTime.Now;
			_lastPathCalculatedAt = DateTime.Now;
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
			if (_followTarget != null)
			{
				//Unsure if I have to generate a new Vector2 to avoid reading live memory values but lets leave it for now.
				_lastKnownTargetPosition = new Vector2(_followTarget.GridPos.X, _followTarget.GridPos.Y);

				//Check if we have an active path. 
				//No path and min distance: Create new
				//Existing path and repath elapsed: Create new with buffered starting pos
				if (_path != null)
				{
					if (DateTime.Now > _lastPathCalculatedAt.AddMilliseconds(Settings.CalculatePathFrequency.Value))
					{
						var lastNode = _path.Last();
						var distanceFromEndOfPath = Vector2.Distance(new Vector2(lastNode.x, lastNode.y), _lastKnownTargetPosition);
						var distanceFromCurrent = Vector2.Distance(GameController.Player.GridPos, _lastKnownTargetPosition);

						//recalculate ONLY if both positions are greater than threshold

						if (distanceFromEndOfPath > 50 && distanceFromCurrent > 50)
						{
							_lastPathCalculatedAt = DateTime.Now.AddSeconds(5);

							//Do not block main thread for pathfinding.
							new Task(() =>
							{
								GeneratePath(GameController.Player.GridPos, _lastKnownTargetPosition);
							}).Start();
						}
					}
					Pathfind();
				}
				else if(Vector2.Distance(GameController.Player.GridPos, _lastKnownTargetPosition) > 50 && DateTime.Now > _lastPathCalculatedAt.AddMilliseconds(Settings.CalculatePathFrequency.Value))
				{					
					//_lastPathCalcualtedAt will be reset at end of generating path. Set it to a large value here to block thread conflicts.
					_lastPathCalculatedAt = DateTime.Now.AddSeconds(5);


					//Do not block main thread for pathfinding.
					new Task(() =>
					{
						GeneratePath(GameController.Player.GridPos, _lastKnownTargetPosition);
					}).Start();
				}
			}
			else if(_hasSuccessfullyFollowed && DateTime.Now > _lastMovementKey.AddMilliseconds(250 + random.Next(250)))
			{
				_lastMovementKey = DateTime.Now;
				//Check if there's a nearby transition and use it.
				var transitionTarget = _areaTransitions.Values.OrderBy(I => Vector2.Distance(GameController.Player.GridPos, I)).FirstOrDefault();
				if (Vector2.Distance(GameController.Player.GridPos, transitionTarget) < 25)
				{
					Input.KeyUp(Settings.MovementKey);
					Mouse.SetCursorPosAndLeftClickHuman(GridToValidScreenPos(transitionTarget), 100);
				}
			}
			return null;
		}

		private Entity GetFollowingTarget()
		{
			var leaderName = Settings.FollowTarget.Value.ToLower();
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

		private void GeneratePath(Vector2 p1, Vector2 p2)
		{
			//This is run in other thread. Data could have changed so just trycatch it for now.
			try
			{
				var startPos = GetMinDistancePath(10);
				var endpos = new GridPos((int)p2.X, (int)p2.Y);
				///This is not supposed to be necessary but I had issues where pathfinding wasn't usable a second time.
				//Need to look into it as this is stupid
				var map = _mapGrid.Clone();
				var pathParams = new JumpPointParam(map, startPos, endpos, EndNodeUnWalkableTreatment.ALLOW);
				var newPath = JumpPointFinder.FindPath(pathParams);
				_path = newPath;
				_lastPathCalculatedAt = DateTime.Now;
			}
			catch { }
		}

		/// <summary>
		/// Returns a starting position for use in pathfinding. If we have an existin gpath, choose a node that's at least 'minDist' units away from current location
		/// This is to stop pathing backwards once the new path is finished generating (player will already have moved a decent amount of units)
		/// </summary>
		/// <param name="minDist">Minimum distance on current path we want used as the startingPos for next path calculation</param>
		/// <returns>Position to start next pathfinding from.</returns>
		private GridPos GetMinDistancePath(int minDist)
		{
			var startPos = new GridPos((int)GameController.Player.GridPos.X, (int)GameController.Player.GridPos.Y);
			var distanceSum = 0.0;

			
			if(_path != null)
				for(var i =1; i < _path.Count; i++)
				{
					distanceSum += Vector2.Distance(new Vector2((int)_path[i-1].x, (int)_path[i-1].y), new Vector2((int)_path[i].x, (int)_path[i].y));
					if (distanceSum >= minDist)
					{
						startPos = _path[i];
						break;
					}
				}

			return startPos;
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
							GeneratePNG();
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
			Graphics.DrawText($"Follow Enabled: {Settings.IsFollowEnabled.Value}", new Vector2(100, 20));
			var counter = 0;
			foreach (var transition in _areaTransitions)
			{
				counter++;
				Graphics.DrawText($"{transition.Key} at { transition.Value.X} { transition.Value.Y}", new Vector2(100, 20 + counter * 20));
			}
		}

		private void Pathfind()
		{
			if (_path == null || _path.Count == 0)
				return;

			//Pointless. This is just so we know if we've moved successfully at least once on this map.
			_hasSuccessfullyFollowed = true;

			//Select the next point in our current pathfinding
			var next = _path.First();
			//Calculate distance to next node
			var dist = Vector2.Distance(GameController.Player.GridPos, new Vector2(next.x, next.y));

			//If the node is too close and more nodes remain, skip it. 
			/*while (_path.Count > 1 && dist < Settings.PathfindingNodeDistance.Value)
			{
				_path.RemoveAt(0);
				next = _path.First();
				dist = Vector2.Distance(GameController.Player.GridPos, new Vector2(next.x, next.y));
			}*/

			//Get valid mouse position for hover
			var cameraPos = GridToValidScreenPos(new Vector2(next.x, next.y));

			Graphics.DrawText($"Pathfinding Target: {cameraPos} Distance: {dist} Path Node Count: {_path.Count}", new Vector2(100, 200));
			Mouse.SetCursorPosHuman2(cameraPos);
			if (DateTime.Now > _lastMovementKey.AddMilliseconds(50 + random.Next(30)))
			{
				Input.KeyPress(Settings.MovementKey);
				System.Threading.Thread.Sleep(random.Next(20) + 20);
				Input.KeyPressRelease(Settings.MovementKey);
				_lastMovementKey = DateTime.Now;
			}

			if (dist < Settings.PathfindingNodeDistance.Value)
				_path.RemoveAt(0);

			//Clean up finished path
			if (_path.Count == 0)
			{
				Input.KeyUp(Settings.MovementKey);
				_path = null;

				//Handle targeting transition point (do not return to pathfinding)
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