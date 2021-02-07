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
		private Camera Camera => GameController.Game.IngameState.Camera;		
		private Dictionary<string, Vector3> _areaTransitions = new Dictionary<string, Vector3>();
		private Random random = new Random();
		private List<Vector3> _targetPositions = new List<Vector3>();
		private Vector3 _lastTargetPosition;
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

			return base.Initialise();
		}


		/// <summary>
		/// Clears all pathfinding values. Used on area transitions primarily.
		/// </summary>
		private void ResetPathing()
		{
			_targetPositions = new List<Vector3>();
			_followTarget = null;
			_lastTargetPosition = Vector3.Zero;
			_areaTransitions = new Dictionary<string, Vector3>();
		}

		public override void AreaChange(AreaInstance area)
		{
			ResetPathing();
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
				if( Vector3.Distance(GameController.Player.Pos,_followTarget.Pos) >= Settings.ClearPathDistance.Value)
				{

					if (_targetPositions.Count > 0 && Vector3.Distance(_targetPositions.Last(),_followTarget.Pos) > Settings.PathfindingNodeDistance)
						_targetPositions.Add(_followTarget.Pos);
					else if(_targetPositions.Count == 0)
						_targetPositions.Add(_followTarget.Pos);
				}

				//If we're already close to the target, clear our path. 
				if (Vector3.Distance(GameController.Player.Pos,_followTarget.Pos) <= Settings.ClearPathDistance)				
					_targetPositions = new List<Vector3>();
				
				_lastTargetPosition = _followTarget.Pos;
			}

			//Check last movement input time
			if (DateTime.Now > _nextBotAction)
			{
				//Check if any paths remain. 
				if (_targetPositions.Count > 1 &&Vector3.Distance(GameController.Player.Pos,_targetPositions[0]) < Settings.PathfindingNodeDistance)
					_targetPositions.RemoveAt(0);

				//Check if we have a path remaining
				if (_targetPositions.Count > 0)
				{
					_nextBotAction = DateTime.Now.AddMilliseconds(Settings.BotInputFrequency / 2 + random.Next(Settings.BotInputFrequency / 2));
					var next = _targetPositions[0];
					var cameraPos = WorldToValidScreenPosition(next);
					Mouse.SetCursorPosHuman2(cameraPos);
					System.Threading.Thread.Sleep(random.Next(25) + 30);
					Input.KeyDown(Settings.MovementKey);
					System.Threading.Thread.Sleep(random.Next(25) + 30);
					Input.KeyUp(Settings.MovementKey);

					if (Vector3.Distance(GameController.Player.Pos,next) < Settings.PathfindingNodeDistance)
						_targetPositions.RemoveAt(0);
				}

				else if (_followTarget == null &&
					_areaTransitions.Count > 0 &&
					_lastTargetPosition != Vector3.Zero)
				{
					_nextBotAction = DateTime.Now.AddMilliseconds(Settings.BotInputFrequency * 5 + random.Next(Settings.BotInputFrequency * 5));
					//Check if we're too far away from the transition. If os move towards last known first. 
					if (Vector3.Distance(_lastTargetPosition,GameController.Player.Pos) > Settings.ClearPathDistance)
					{
						Mouse.SetCursorPosHuman2(WorldToValidScreenPosition(_lastTargetPosition));
						System.Threading.Thread.Sleep(random.Next(25) + 30);
						Input.KeyDown(Settings.MovementKey);
						System.Threading.Thread.Sleep(random.Next(25) + 30);
						Input.KeyUp(Settings.MovementKey);
					}
					else
					{
						var transitionTarget = _areaTransitions.Values.OrderBy(I => Vector3.Distance(I, GameController.Player.Pos)).FirstOrDefault();
						if (Vector3.Distance(transitionTarget,_lastTargetPosition) <= Settings.ClearPathDistance)
						{
							Input.KeyUp(Settings.MovementKey);
							Mouse.SetCursorPosAndLeftClickHuman(WorldToValidScreenPosition(transitionTarget), 100);
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
							_areaTransitions.Add(entity.RenderName, entity.Pos);
							
						}
						break;
				}
			base.EntityAdded(entity);
		}


		public override void Render()
		{
			if (_targetPositions != null && _targetPositions.Count > 1)
				for (var i = 1; i < _targetPositions.Count; i++)
				{
					var start = WorldToValidScreenPosition( _targetPositions[i - 1]);
					var end = WorldToValidScreenPosition(_targetPositions[i]);
					Graphics.DrawLine(start, end, 2, SharpDX.Color.Pink);
				}
			var dist = _targetPositions.Count > 0 ? Vector3.Distance(GameController.Player.Pos,_targetPositions.First()): 0;
			var targetDist = _lastTargetPosition == null ? "NA" : Vector3.Distance(GameController.Player.Pos, _lastTargetPosition).ToString();
			Graphics.DrawText($"Follow Enabled: {Settings.IsFollowEnabled.Value}", new Vector2(500, 120));
			Graphics.DrawText($"Pathing Waypoints: {_targetPositions.Count} Next WP Distance: {dist} Target Distance: {targetDist}", new Vector2(500, 140));
			var counter = 0;
			foreach (var transition in _areaTransitions)
			{
				counter++;
				Graphics.DrawText($"{transition.Key} at { transition.Value.X} { transition.Value.Y}", new Vector2(100, 120 + counter * 20));
			}
		}


		private Vector2 WorldToValidScreenPosition(Vector3 worldPos)
		{
			var cameraPos = Camera.WorldToScreen(worldPos);
			
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