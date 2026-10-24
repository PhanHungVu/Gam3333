using System;
using System.Collections.Generic;
using UnityEngine;

// Pick an authored composition once per board. Papers are never independently jittered.
[DisallowMultipleComponent]
public sealed class PreMissionNoticeLayout : MonoBehaviour
{
    [Serializable]
    public sealed class LayoutDesign
    {
        public string name;
        public bool mainOnRight;
        [Range(-1.25f, 1.25f)] public float mainAngle;
        public float[] sideAngles;
    }
    [SerializeField, Range(0.3f, 0.5f)] float mainWidthFraction = 0.42f;
    [SerializeField, Min(0f)] float spacing = 20f;
    [SerializeField] LayoutDesign[] designs =
    {
        new LayoutDesign { name = "Aligned", sideAngles = new[] { 0f, 0f, 0f, 0f, 0f, 0f } },
        new LayoutDesign { name = "Framed", mainAngle = -0.6f, sideAngles = new[] { -0.8f, 0.8f, -0.8f, 0.8f, 0f, 0f } },
        new LayoutDesign { name = "Rows", mainOnRight = true, mainAngle = 0.5f, sideAngles = new[] { 0.65f, 0.65f, -0.65f, -0.65f, 0.65f, 0.65f } }
    };
    readonly List<QuestNoticeView> notices = new List<QuestNoticeView>();
    LayoutDesign selectedDesign;
    public int SelectedDesignIndex { get; private set; }
    public int DesignCount => designs != null ? designs.Length : 0;
    public string SelectedDesignName => selectedDesign != null ? selectedDesign.name : "Aligned";

    public void Configure(IReadOnlyList<QuestNoticeView> offers, int seed)
    {
        notices.Clear(); notices.AddRange(offers);
        SelectedDesignIndex = DesignCount > 0 ? new System.Random(seed).Next(DesignCount) : 0;
        selectedDesign = DesignCount > 0 ? designs[SelectedDesignIndex] : null;
        Refresh();
    }
    void OnEnable() => Refresh();
    void OnRectTransformDimensionsChange() => Refresh();
    public void Refresh()
    {
        var area = ((RectTransform)transform).rect;
        if (area.width <= 0 || area.height <= 0 || notices.Count == 0) return;
        int featured = notices.FindIndex(n => n != null && n.Quest.category == QuestCategory.Main);
        Rect ordinaryArea = area;
        if (featured >= 0 && notices.Count > 1)
        {
            Rect main;
            if (area.width / area.height >= 1.45f)
            {
                float width = (area.width - spacing) * mainWidthFraction;
                bool right = selectedDesign != null && selectedDesign.mainOnRight;
                main = new Rect(right ? area.xMax - width : area.xMin, area.yMin, width, area.height);
                ordinaryArea = new Rect(right ? area.xMin : main.xMax + spacing, area.yMin, area.width - width - spacing, area.height);
            }
            else
            {
                float height = (area.height - spacing) * 0.36f;
                main = new Rect(area.xMin, area.yMax - height, area.width, height);
                ordinaryArea = new Rect(area.xMin, area.yMin, area.width, area.height - height - spacing);
            }
            Fit(featured, main, true, selectedDesign != null ? selectedDesign.mainAngle : 0f);
        }
        int count = notices.Count - (featured >= 0 && notices.Count > 1 ? 1 : 0);
        int columns = Mathf.Min(count, featured >= 0 || count <= 4 ? 2 : 3);
        if (ordinaryArea.width < 450f) columns = 1;
        columns = Mathf.Max(1, columns);
        int rows = Mathf.CeilToInt((float)count / columns);
        float cellWidth = (ordinaryArea.width - spacing * (columns - 1)) / columns;
        float cellHeight = (ordinaryArea.height - spacing * (rows - 1)) / rows;
        int slot = 0;
        for (int i = 0; i < notices.Count; i++)
        {
            if (i == featured && notices.Count > 1) continue;
            int column = slot % columns, row = slot / columns;
            int rowCount = Mathf.Min(columns, count - row * columns);
            float rowWidth = rowCount * cellWidth + (rowCount - 1) * spacing;
            float x = ordinaryArea.center.x - rowWidth * 0.5f + column * (cellWidth + spacing);
            float angle = 0f;
            if (selectedDesign != null && selectedDesign.sideAngles != null && selectedDesign.sideAngles.Length > 0 && rowCount > 1)
                angle = selectedDesign.sideAngles[slot % selectedDesign.sideAngles.Length];
            if (i == featured && selectedDesign != null) angle = selectedDesign.mainAngle;
            Fit(i, new Rect(x, ordinaryArea.yMax - (row + 1) * cellHeight - row * spacing, cellWidth, cellHeight), i == featured, angle);
            slot++;
        }
    }
    void Fit(int index, Rect cell, bool featured, float angle)
    {
        if (notices[index] == null) return;
        angle = Mathf.Clamp(angle, -1.25f, 1.25f);
        Vector2 size = new Vector2(cell.width * 0.91f, cell.height * (featured ? 0.87f : 0.90f));
        float sin = Mathf.Abs(Mathf.Sin(angle * Mathf.Deg2Rad)), cos = Mathf.Abs(Mathf.Cos(angle * Mathf.Deg2Rad));
        Vector2 bounds = new Vector2(size.x * cos + size.y * sin, size.x * sin + size.y * cos);
        size *= Mathf.Min(1f, cell.width * 0.94f / bounds.x, cell.height * 0.94f / bounds.y);
        notices[index].SetBoardPose(cell.center, size, angle, featured);
    }
}
