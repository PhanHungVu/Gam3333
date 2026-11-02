using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Height-aware pathing: generated terrain's cliffs block NavGrid cells, obstacles are measured from
// the ground under them, enemies spawn on the terrain, and flat scenes are unchanged without terrain.
public static class NavTerrainChecks
{
    public static void Run(Action<bool, string> check)
    {
        var site = new Vector3(93000f, 0f, 0f);
        GameObject gridObject = null, owner = null, plateauBox = null, lowBox = null;
        try
        {
            gridObject = new GameObject("NavTerrain Check Grid");
            var grid = gridObject.AddComponent<NavGrid>();
            var settings = new SerializedObject(grid);
            settings.FindProperty("origin").vector3Value = site;
            settings.FindProperty("width").floatValue = 40f;
            settings.FindProperty("depth").floatValue = 40f;
            settings.FindProperty("cellSize").floatValue = 2f;
            settings.FindProperty("drawOpenCells").boolValue = false;
            settings.FindProperty("drawBlockedCells").boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo();

            // A box standing on low ground (y 0..2), and one standing on a 20 m plateau (y 20..22).
            lowBox = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lowBox.tag = "Obstacle"; lowBox.transform.position = site + new Vector3(-10f, 1f, -10f); lowBox.transform.localScale = new Vector3(2f, 2f, 2f);
            plateauBox = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plateauBox.tag = "Obstacle"; plateauBox.transform.position = site + new Vector3(-10f, 21f, 10f); plateauBox.transform.localScale = new Vector3(2f, 2f, 2f);
            Physics.SyncTransforms();

            grid.Bake();
            check(grid.IsBlockedWorld(lowBox.transform.position) && !grid.IsBlockedWorld(plateauBox.transform.position)
                && !grid.IsBlockedWorld(site + new Vector3(10f, 0f, 0f)),
                "Without generated terrain NavGrid bakes as before (one floor at the grid's height)");
            check(Mathf.Approximately(EnemyAgent.SpawnGroundY(site, 0f), 0f), "Without terrain, enemies start on the fixed Ground Y");

            // Terrain: everything east of the site is a cliff; the north half is a 20 m plateau.
            owner = new GameObject("NavTerrain Check Owner");
            NavTerrain.Register(owner, p => p.x > site.x + 5f, p => p.z > site.z ? 20f : 0f);
            grid.Bake();
            check(grid.IsBlockedWorld(site + new Vector3(10f, 0f, 0f)) && !grid.IsBlockedWorld(site + new Vector3(-2f, 0f, -2f)),
                "Terrain cliffs block their NavGrid cells");
            check(grid.IsBlockedWorld(plateauBox.transform.position) && grid.IsBlockedWorld(lowBox.transform.position),
                "Obstacles count from the ground under them, so cover on a plateau blocks too");
            check(Mathf.Approximately(EnemyAgent.SpawnGroundY(site + new Vector3(0f, 50f, 10f), 0f), 20f),
                "Enemies start on the terrain's height, not the fixed Ground Y");

            // A generated map sizes the grid: it then covers exactly the new area.
            grid.Resize(site + new Vector3(-100f, 0f, 0f), 20f, 20f);
            check(grid.Baked && !grid.IsBlockedWorld(site + new Vector3(-95f, 0f, 0f)) && grid.IsBlockedWorld(site + new Vector3(-85f, 0f, 0f)),
                "NavGrid resizes to a new area and re-bakes, so it fits a generated map");

            var map = new TerrainMap(2, 2, 4f, -4f, -4f);
            map.Walkable[0] = true; map.Walkable[1] = false; map.Walkable[2] = true; map.Walkable[3] = true;
            check(!map.WallAt(-2f, -2f) && map.WallAt(2f, -2f) && map.WallAt(10f, 0f),
                "A generated map reports cliffs and anywhere off the map as walls");
        }
        finally
        {
            if (owner != null) NavTerrain.Unregister(owner);
            foreach (var go in new[] { gridObject, owner, plateauBox, lowBox }) if (go != null) Object.DestroyImmediate(go);
        }
    }
}
