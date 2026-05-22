using System.Collections.Generic;
using UnityEngine;

public class ChestGeometryModel
{
    // Builds chest geometry data from latent size parameters.
    public ChestGeometryData Build(ChestLatentParams p)
    {
        // Keep parameters in a valid range before creating points.
        p.ClampValues();

        ChestGeometryData data = new ChestGeometryData();

        AddBodyPoints(data, p);
        AddBodyFaces(data);

        return data;
    }

    private void AddBodyPoints(ChestGeometryData data, ChestLatentParams p)
    {
        // Main dimensions; taper narrows the lower/back part of the body.
        float w = p.width;
        float h = p.height;
        float d = p.depth;
        float t = p.taper;

        // Front four body corners.
        data.AddPoint("BD_TL", new Vector3(-w / 2f, 0f, 0f));
        data.AddPoint("BD_TR", new Vector3(w / 2f, 0f, 0f));
        data.AddPoint("BD_BL", new Vector3((t - w) / 2f, -h, t));
        data.AddPoint("BD_BR", new Vector3((w - t) / 2f, -h, t));

        // Back four body corners, extended along the z axis.
        data.AddPoint("BD_TL_1", new Vector3(-w / 2f, 0f, d));
        data.AddPoint("BD_TR_1", new Vector3(w / 2f, 0f, d));
        data.AddPoint("BD_BL_1", new Vector3((t - w) / 2f, -h, d - t));
        data.AddPoint("BD_BR_1", new Vector3((w - t) / 2f, -h, d - t));
        
    }

    private void AddBodyFaces(ChestGeometryData data)
    {
        // Faces reuse named points; the final number is used by the renderer.
        data.AddFace(
            "Body_Top",
            new List<string>
            {
                "BD_TL",
                "BD_TR",
                "BD_TR_1",
                "BD_TL_1"
            },
            2
        );

        data.AddFace(
            "Body_front",
            new List<string>
            {
                "BD_TL",
                "BD_TR",
                "BD_BR",
                "BD_BL"
            },
            2
        );

        data.AddFace(
            "Body_right",
            new List<string>
            {
                "BD_TR",
                "BD_TR_1",
                "BD_BR_1",
                "BD_BR"
            },
            2
        );

        data.AddFace(
            "Body_left",
            new List<string>
            {   "BD_BL",
                "BD_BL_1",
                "BD_TL_1",
                "BD_TL"
            },
            1
        );

        data.AddFace(
            "Body_bottom",
            new List<string>
            {
                "BD_BL",
                "BD_BL_1",
                "BD_BR_1",
                "BD_BR"
            },
            0
        );

    }
}
