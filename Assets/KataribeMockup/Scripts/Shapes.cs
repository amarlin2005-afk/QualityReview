using UnityEngine;

/// <summary>モックアップ用の仮ビジュアル。すべて LineRenderer で描く。</summary>
public static class Shapes
{
    static Material material;

    public static Material Material
    {
        get
        {
            if (material == null)
            {
                Shader sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                if (sh == null) sh = Shader.Find("Sprites/Default");
                if (sh == null) Debug.LogError("線を描くシェーダーが見つかりません");
                material = new Material(sh);
            }
            return material;
        }
    }

    public static LineRenderer CreateLine(Transform parent, string name, Color color, float width, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial = Material;
        lr.useWorldSpace = false;
        lr.startWidth = width;
        lr.endWidth = width;
        lr.startColor = color;
        lr.endColor = color;
        lr.numCapVertices = 6;
        lr.numCornerVertices = 6;
        lr.sortingOrder = order;
        lr.positionCount = 0;
        return lr;
    }

    public static LineRenderer CreateSegment(Transform parent, string name, Vector3 a, Vector3 b, Color color, float width, int order)
    {
        var lr = CreateLine(parent, name, color, width, order);
        lr.numCapVertices = 0;
        lr.positionCount = 2;
        lr.SetPosition(0, a);
        lr.SetPosition(1, b);
        return lr;
    }

    /// <summary>輪。width を radius*2 にすると塗りつぶしの円になる。</summary>
    public static LineRenderer CreateCircle(Transform parent, string name, float radius, Color color, float width, int order, int segments = 32)
    {
        var lr = CreateLine(parent, name, color, width, order);
        lr.loop = true;
        lr.numCapVertices = 0;
        lr.positionCount = segments;
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            lr.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f));
        }
        return lr;
    }

    public static LineRenderer CreateDisk(Transform parent, string name, float radius, Color color, int order)
    {
        return CreateCircle(parent, name, radius * 0.5f, color, radius, order);
    }

    public static void SetColor(LineRenderer lr, Color c)
    {
        lr.startColor = c;
        lr.endColor = c;
    }
}
