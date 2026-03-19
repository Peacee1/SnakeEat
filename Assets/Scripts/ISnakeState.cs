using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ISnakeState exposes the minimum state that FoodSpawner needs to query
/// without being directly coupled to SnakeController.
/// This follows the Dependency Inversion Principle (SOLID - D).
/// </summary>
public interface ISnakeState
{
    /// <summary>Returns all grid cells currently occupied by the snake (head + body).</summary>
    IReadOnlyList<Vector2Int> OccupiedCells { get; }
}
