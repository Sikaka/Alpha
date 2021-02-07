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
using Map = ExileCore.PoEMemory.Elements.Map;
using EpPathFinding.cs;
using System.Linq;
using ExileCore.Shared.Helpers;
using System.IO;
using System.Threading;

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
		private Random random = new Random();
		private Camera Camera => GameController.Game.IngameState.Camera;		
		private Dictionary<uint, Vector3> _areaTransitions = new Dictionary<uint, Vector3>();
		
		private Vector3 _lastTargetPosition;
		private Vector3 _lastPlayerPosition;
		private Entity _followTarget;


		private List<TaskNode> _tasks = new List<TaskNode>();
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
			_tasks = new List<TaskNode>();
			_followTarget = null;
			_lastTargetPosition = Vector3.Zero;
			_lastPlayerPosition = Vector3.Zero;
			_areaTransitions = new Dictionary<uint, Vector3>();
		}

		public override void AreaChange(AreaInstance area)
		{
			ResetPathing();

			//Load initial transitions!

			foreach (var transition in GameController.EntityListWrapper.Entities.Where(I => I.Type == ExileCore.Shared.Enums.EntityType.AreaTransition ||
			 I.Type == ExileCore.Shared.Enums.EntityType.Portal ||
			 I.Type == ExileCore.Shared.Enums.EntityType.TownPortal).ToList())
			{
				if(!_areaTransitions.ContainsKey(transition.Id))
					_areaTransitions.Add(transition.Id, transition.Pos);
			}
		}

		public override Job Tick()
		{
			if (Settings.ToggleFollower.PressedOnce())
			{
				Settings.IsFollowEnabled.SetValueNoEvent(!Settings.IsFollowEnabled.Value);
				_tasks = new List<TaskNode>();
			}

			if (!Settings.IsFollowEnabled.Value)
				return null;


			//Cache the current follow target (if present)
			_followTarget = GetFollowingTarget();
			if (_followTarget != null)
			{
				var distanceFromFollower = Vector3.Distance(GameController.Player.Pos, _followTarget.Pos);
				//We are NOT within clear path distance range of leader. Logic can continue
				if (distanceFromFollower >= Settings.ClearPathDistance.Value)
				{
					//Leader moved VERY far in one frame. Check for transition to use to follow them.
					var distanceMoved = Vector3.Distance(_lastTargetPosition, _followTarget.Pos);
					if (_lastTargetPosition != Vector3.Zero &&  distanceMoved > Settings.ClearPathDistance.Value)
					{
						var transition = _areaTransitions.Values.OrderBy(I => Vector3.Distance(_lastTargetPosition, I)).FirstOrDefault();
						var dist = Vector3.Distance(_lastTargetPosition, transition);
						if (Vector3.Distance(_lastTargetPosition, transition) < Settings.ClearPathDistance.Value)
							_tasks.Add(new TaskNode(transition, 200, TaskNodeType.Transition));
					}
					//We have no path, set us to go to leader pos.
					else if (_tasks.Count == 0)
						_tasks.Add(new TaskNode(_followTarget.Pos, Settings.PathfindingNodeDistance));
					//We have a path. Check if the last task is far enough away from current one to add a new task node.
					else
					{
						var distanceFromLastTask = Vector3.Distance(_tasks.Last().WorldPosition, _followTarget.Pos);
						if (distanceFromLastTask >= Settings.PathfindingNodeDistance)
							_tasks.Add(new TaskNode(_followTarget.Pos, Settings.PathfindingNodeDistance));
					}
				}
				else
				{
					//Clear all tasks except for looting/claim portal (as those only get done when we're within range of leader. 
					if(_tasks.Count > 0)					
						for(var i = _tasks.Count - 1; i >=0; i--)
							if(_tasks[i].Type == TaskNodeType.Movement || _tasks[i].Type == TaskNodeType.Transition)							
								_tasks.RemoveAt(i);

					/*
					//Check if we should add quest loot logic. We're close to leader already
					var questLoot = GetLootableQuestItem();
					if (questLoot != null &&
						Vector3.Distance(GameController.Player.Pos, questLoot.Pos) < Settings.ClearPathDistance.Value)
						_tasks.Add(new TaskNode(questLoot.Pos, Settings.ClearPathDistance, TaskNodeType.Loot));
						*/
				}
				_lastTargetPosition = _followTarget.Pos;
			}
			//Leader is null but we have tracked them this map.
			//Try using transition to follow them to their map
			else if (_tasks.Count == 0 &&
				_lastTargetPosition != Vector3.Zero)
			{
				var transition = _areaTransitions.Values.OrderBy(I => Vector3.Distance(_lastTargetPosition, I)).FirstOrDefault();
				if (Vector3.Distance(_lastTargetPosition, transition) < Settings.ClearPathDistance.Value)
					_tasks.Add(new TaskNode(transition, Settings.PathfindingNodeDistance.Value, TaskNodeType.Transition));
			}


			//We have our tasks, now we need to perform in game logic with them.
			if (DateTime.Now > _nextBotAction && _tasks.Count > 0)
			{
				var currentTask = _tasks.First();
				var taskDistance = Vector3.Distance(GameController.Player.Pos, currentTask.WorldPosition);
				var playerDistanceMoved = Vector3.Distance(GameController.Player.Pos, _lastPlayerPosition);

				//We are using a same map transition and have moved significnatly since last tick. Mark the transition task as done.
				if (currentTask.Type == TaskNodeType.Transition && 
					playerDistanceMoved >= Settings.ClearPathDistance.Value)
				{
					_tasks.RemoveAt(0);
					if (_tasks.Count > 0)
						currentTask = _tasks.First();
					else
					{
						_lastPlayerPosition = GameController.Player.Pos;
						return null;
					}
				}

				switch (currentTask.Type)
				{
					case TaskNodeType.Movement:
						_nextBotAction = DateTime.Now.AddMilliseconds(Settings.BotInputFrequency + random.Next(Settings.BotInputFrequency));
						Mouse.SetCursorPosHuman2(WorldToValidScreenPosition(currentTask.WorldPosition));
						Thread.Sleep(random.Next(25) + 30);
						Input.KeyDown(Settings.MovementKey);
						Thread.Sleep(random.Next(25) + 30);
						Input.KeyUp(Settings.MovementKey);

						//Within bounding range. Task is complete
						if (taskDistance <= Settings.PathfindingNodeDistance.Value)
							_tasks.RemoveAt(0);
						break;
					case TaskNodeType.Loot:
						{
							currentTask.AttemptCount++;
							var questLoot = GetLootableQuestItem();
							if (questLoot == null 
								|| currentTask.AttemptCount > 2
								|| Vector3.Distance(GameController.Player.Pos, questLoot.Pos) >= Settings.ClearPathDistance.Value)
								_tasks.RemoveAt(0);

							Input.KeyUp(Settings.MovementKey);
							HoverToEntityAction(questLoot);
							Mouse.LeftClick();
							_nextBotAction = DateTime.Now.AddMilliseconds(500 + random.Next(Settings.BotInputFrequency.Value));
							break;
						}
					case TaskNodeType.Transition:
						{
							_nextBotAction = DateTime.Now.AddMilliseconds(Settings.BotInputFrequency * 2 + random.Next(Settings.BotInputFrequency));
							var screenPos = WorldToValidScreenPosition(currentTask.WorldPosition);
							if (taskDistance <= Settings.ClearPathDistance.Value)
							{
								//Click the transition
								Input.KeyUp(Settings.MovementKey);
								Mouse.SetCursorPosAndLeftClickHuman(screenPos, 100);
								_nextBotAction = DateTime.Now.AddSeconds(1);
							}
							else
							{
								//Walk towards the transition
								Mouse.SetCursorPosHuman2(screenPos);
								Thread.Sleep(random.Next(25) + 30);
								Input.KeyDown(Settings.MovementKey);
								Thread.Sleep(random.Next(25) + 30);
								Input.KeyUp(Settings.MovementKey);
							}							
							currentTask.AttemptCount++;
							if (currentTask.AttemptCount > 3)
								_tasks.RemoveAt(0);
							break;
						}
				}
			}
			_lastPlayerPosition = GameController.Player.Pos;
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
		private bool HoverToEntityAction(Entity entity)
		{
			Random rnd = new Random();
			int offsetValue = 10;

			// Matrix of offsets as vectors. Try each offset and see whether the entity's isTargeted is true
			List<Vector2> offsets = new List<Vector2>();

			foreach (int yOffset in Enumerable.Range(-5, 5))
				foreach (int xOffset in Enumerable.Range(-5, 5))
					offsets.Add(new Vector2(xOffset * offsetValue, yOffset * offsetValue));

			bool targeted = false;

			HoverTo(entity);

			while (offsets.Any())
			{
				if (entity.GetComponent<Targetable>().isTargeted)
				{
					targeted = true;
					break;
				}

				// If entity is not present anymore (e.g. map portal is used by another player) stop hovering
				if (!IsEntityPresent(entity.Id)) break;

				int elem = rnd.Next(offsets.Count);
				Vector2 offset = offsets[elem];
				offsets.Remove(offset);

				HoverTo(entity, (int)offset.X, (int)offset.Y);
				Thread.Sleep(50);
			}

			Thread.Sleep(50);

			return targeted;
		}
		private void HoverTo(Entity entity, int xOffset = 0, int yOffset = 0)
		{
			//LogMsgWithVerboseDebug("HoverTo called");

			if (entity == null) return;

			Camera camera = GameController.Game.IngameState.Camera;
			Vector2 windowOffset = GameController.Window.GetWindowRectangle().TopLeft;

			Vector2 result = camera.WorldToScreen(entity.Pos);

			int randomXOffset = new Random().Next(0, Settings.RandomClickOffset.Value);
			int randomYOffset = new Random().Next(0, Settings.RandomClickOffset.Value);

			Vector2 finalPos = new Vector2(
				result.X + randomXOffset + xOffset + windowOffset.X,
				result.Y + randomYOffset + yOffset + windowOffset.Y);

			bool intersects =
				GameController.Window.GetWindowRectangleTimeCache.Intersects(new RectangleF(finalPos.X, finalPos.Y, 3,
					3));
			// The entity is inside the game window and visible, we can just hover
			if (intersects)
			{
				Mouse.SetCursorPosHuman2(finalPos);
				return;
			}

			// The entity is outside of the visibility. Make some calculations to click within the game window borders
			int smallOffset = 5;

			float topLeftX = GameController.Window.GetWindowRectangle().TopLeft.X;
			float topLeftY = GameController.Window.GetWindowRectangle().TopLeft.Y;
			float bottomRightX = GameController.Window.GetWindowRectangle().BottomRight.X;
			float bottomRightY = GameController.Window.GetWindowRectangle().BottomRight.Y;

			if (finalPos.X < topLeftX) finalPos.X = topLeftX + smallOffset;
			if (finalPos.Y < topLeftY) finalPos.Y = topLeftY + smallOffset;
			if (finalPos.X > bottomRightX) finalPos.X = bottomRightX - smallOffset;

			if (finalPos.Y > bottomRightY) finalPos.Y = bottomRightY - smallOffset;

			Mouse.SetCursorPosHuman2(finalPos);
		}

		private bool IsEntityPresent(uint entityId)
		{
			bool isEntityPresent = false;
			try
			{
				isEntityPresent = GameController.Entities.Any(e => e.Id == entityId);
			}
			catch
			{
			}

			return isEntityPresent;
		}


		private Entity GetLootableQuestItem()
		{
			try
			{
				return GameController.EntityListWrapper.Entities
					.Where(e => e.Type == ExileCore.Shared.Enums.EntityType.WorldItem)
					.Where(e => e.IsTargetable)
					.Where(e => e.GetComponent<WorldItem>() != null)
					.FirstOrDefault(e =>
					{
						Entity itemEntity = e.GetComponent<WorldItem>().ItemEntity;
						return GameController.Files.BaseItemTypes.Translate(itemEntity.Path).ClassName ==
								"QuestItem";
					});
			}
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
						if (!_areaTransitions.ContainsKey(entity.Id))						
							_areaTransitions.Add(entity.Id, entity.Pos);
						break;
				}
			base.EntityAdded(entity);
		}


		public override void Render()
		{
			if (_tasks != null && _tasks.Count > 1)
				for (var i = 1; i < _tasks.Count; i++)
				{
					var start = WorldToValidScreenPosition(_tasks[i - 1].WorldPosition);
					var end = WorldToValidScreenPosition(_tasks[i].WorldPosition);
					Graphics.DrawLine(start, end, 2, SharpDX.Color.Pink);
				}
			var dist = _tasks.Count > 0 ? Vector3.Distance(GameController.Player.Pos, _tasks.First().WorldPosition): 0;
			var targetDist = _lastTargetPosition == null ? "NA" : Vector3.Distance(GameController.Player.Pos, _lastTargetPosition).ToString();
			Graphics.DrawText($"Follow Enabled: {Settings.IsFollowEnabled.Value}", new Vector2(500, 120));
			Graphics.DrawText($"Task Count: {_tasks.Count} Next WP Distance: {dist} Target Distance: {targetDist}", new Vector2(500, 140));
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