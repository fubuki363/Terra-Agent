using System;
using System.Collections.Generic;
using System.Linq;

namespace TerraWikiOverlay;

internal sealed record NavigationTarget(
    string Kind,
    string Label,
    int TileX,
    int TileY,
    string Detail);

internal readonly record struct NavigationSnapshot(
    bool Active,
    string Kind,
    string Label,
    string Detail,
    int TargetIndex,
    int TargetCount,
    float PlayerTileX,
    float PlayerTileY,
    int TargetTileX,
    int TargetTileY,
    float DeltaX,
    float DeltaY,
    float DistanceTiles,
    DateTime UpdatedUtc)
{
    public float AngleRadians => MathF.Atan2(DeltaY, DeltaX);
}

internal sealed class NavigationState
{
    private readonly object _sync = new();
    private List<NavigationTarget> _targets = new();
    private int _targetIndex;
    private float _playerTileX;
    private float _playerTileY;
    private DateTime _updatedUtc;

    public bool IsActive
    {
        get { lock (_sync) return _targets.Count > 0; }
    }

    public void SetTargets(IEnumerable<NavigationTarget> targets, float playerTileX, float playerTileY)
    {
        lock (_sync)
        {
            _targets = targets
                .GroupBy(target => (target.Kind, target.TileX, target.TileY))
                .Select(group => group.First())
                .Take(24)
                .ToList();
            _targetIndex = 0;
            _playerTileX = playerTileX;
            _playerTileY = playerTileY;
            _updatedUtc = DateTime.UtcNow;
        }
    }

    public void UpdatePlayer(float playerTileX, float playerTileY)
    {
        lock (_sync)
        {
            _playerTileX = playerTileX;
            _playerTileY = playerTileY;
            _updatedUtc = DateTime.UtcNow;
        }
    }

    public bool MoveNext()
    {
        lock (_sync)
        {
            if (_targets.Count <= 1)
                return false;
            _targetIndex = (_targetIndex + 1) % _targets.Count;
            return true;
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _targets.Clear();
            _targetIndex = 0;
        }
    }

    public NavigationSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            if (_targets.Count == 0)
                return default;
            NavigationTarget target = _targets[Math.Clamp(_targetIndex, 0, _targets.Count - 1)];
            float dx = target.TileX - _playerTileX;
            float dy = target.TileY - _playerTileY;
            return new NavigationSnapshot(
                true,
                target.Kind,
                target.Label,
                target.Detail,
                _targetIndex + 1,
                _targets.Count,
                _playerTileX,
                _playerTileY,
                target.TileX,
                target.TileY,
                dx,
                dy,
                MathF.Sqrt(dx * dx + dy * dy),
                _updatedUtc);
        }
    }
}
