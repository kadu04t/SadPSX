namespace SadPSX.Core.Gpu;

public sealed partial class Gpu
{
    private static readonly int[,] DitherMatrix =
    {
        { -4, 0, -3, 1 },
        { 2, -2, 3, -1 },
        { -3, 1, -4, 0 },
        { 3, -1, 2, -2 },
    };

    private readonly record struct RgbColor(int Red, int Green, int Blue);

    private readonly record struct RasterVertex(
        int PixelX,
        int PixelY,
        RgbColor Color,
        int TextureU,
        int TextureV);

    private void DrawPolygon(IReadOnlyList<uint> packet)
    {
        uint commandWord = packet[0];
        bool gouraud = (commandWord & (1u << 28)) != 0;
        bool quadrilateral = (commandWord & (1u << 27)) != 0;
        bool textured = (commandWord & (1u << 26)) != 0;
        bool semiTransparent = (commandWord & (1u << 25)) != 0;
        bool rawTexture = (commandWord & (1u << 24)) != 0;
        int vertexCount = quadrilateral ? 4 : 3;
        int packetIndex = 1;
        int colorLookupX = 0;
        int colorLookupY = 0;
        uint texturePage = _internalRegisters[0] & 0x1FF;
        RgbColor currentColor = DecodeRgb(commandWord);
        var vertices = new RasterVertex[vertexCount];

        for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
        {
            if (vertexIndex > 0 && gouraud)
                currentColor = DecodeRgb(packet[packetIndex++]);

            uint coordinateWord = packet[packetIndex++];
            int textureU = 0;
            int textureV = 0;

            if (textured)
            {
                uint textureWord = packet[packetIndex++];
                textureU = (int)(textureWord & 0xFF);
                textureV = (int)((textureWord >> 8) & 0xFF);
                uint textureParameter = textureWord >> 16;

                if (vertexIndex == 0)
                {
                    colorLookupX = (int)(textureParameter & 0x3F) * 16;
                    colorLookupY = (int)((textureParameter >> 6) & 0x1FF);
                }
                else if (vertexIndex == 1)
                {
                    texturePage = textureParameter & 0x1FF;
                    _status = (_status & ~0x1FFu) | texturePage;
                    _internalRegisters[0] =
                        (_internalRegisters[0] & ~0x1FFu) | texturePage;
                }
            }

            vertices[vertexIndex] = CreateVertex(
                coordinateWord,
                currentColor,
                textureU,
                textureV);
        }

        DrawTriangle(
            vertices[0],
            vertices[1],
            vertices[2],
            gouraud,
            textured,
            semiTransparent,
            rawTexture,
            texturePage,
            colorLookupX,
            colorLookupY);

        if (quadrilateral)
        {
            DrawTriangle(
                vertices[1],
                vertices[2],
                vertices[3],
                gouraud,
                textured,
                semiTransparent,
                rawTexture,
                texturePage,
                colorLookupX,
                colorLookupY);
        }
    }

    private void DrawTriangle(
        RasterVertex first,
        RasterVertex second,
        RasterVertex third,
        bool gouraud,
        bool textured,
        bool semiTransparent,
        bool rawTexture,
        uint texturePage,
        int colorLookupX,
        int colorLookupY)
    {
        double area = Edge(first, second, third.PixelX, third.PixelY);
        if (area == 0)
            return;

        if (area < 0)
        {
            (second, third) = (third, second);
            area = -area;
        }

        bool dither = (_internalRegisters[0] & (1u << 9)) != 0 &&
            (gouraud || (textured && !rawTexture));

        GetDrawingArea(
            out int drawingLeft,
            out int drawingTop,
            out int drawingRight,
            out int drawingBottom);

        int minimumX = Math.Max(
            drawingLeft,
            Math.Min(first.PixelX, Math.Min(second.PixelX, third.PixelX)));
        int maximumX = Math.Min(
            drawingRight,
            Math.Max(first.PixelX, Math.Max(second.PixelX, third.PixelX)));
        int minimumY = Math.Max(
            drawingTop,
            Math.Min(first.PixelY, Math.Min(second.PixelY, third.PixelY)));
        int maximumY = Math.Min(
            drawingBottom,
            Math.Max(first.PixelY, Math.Max(second.PixelY, third.PixelY)));

        for (int pixelY = minimumY; pixelY <= maximumY; pixelY++)
        {
            for (int pixelX = minimumX; pixelX <= maximumX; pixelX++)
            {
                double sampleX = pixelX + 0.5;
                double sampleY = pixelY + 0.5;
                double firstWeight = Edge(second, third, sampleX, sampleY);
                double secondWeight = Edge(third, first, sampleX, sampleY);
                double thirdWeight = Edge(first, second, sampleX, sampleY);

                if (!IsInsideTopLeft(
                        firstWeight,
                        second,
                        third) ||
                    !IsInsideTopLeft(
                        secondWeight,
                        third,
                        first) ||
                    !IsInsideTopLeft(
                        thirdWeight,
                        first,
                        second))
                {
                    continue;
                }

                firstWeight /= area;
                secondWeight /= area;
                thirdWeight /= area;

                RgbColor color = InterpolateColor(
                    first,
                    second,
                    third,
                    firstWeight,
                    secondWeight,
                    thirdWeight);

                if (!textured)
                {
                    WriteDrawingPixel(
                        pixelX,
                        pixelY,
                        PackColor(color, pixelX, pixelY, dither),
                        semiTransparent,
                        false);
                    continue;
                }

                int textureU = (int)Math.Round(
                    first.TextureU * firstWeight +
                    second.TextureU * secondWeight +
                    third.TextureU * thirdWeight);
                int textureV = (int)Math.Round(
                    first.TextureV * firstWeight +
                    second.TextureV * secondWeight +
                    third.TextureV * thirdWeight);

                if (!TrySampleTexture(
                        textureU,
                        textureV,
                        texturePage,
                        colorLookupX,
                        colorLookupY,
                        out ushort texel))
                {
                    continue;
                }

                ushort source = rawTexture
                    ? texel
                    : ModulateTexture(
                        texel,
                        color,
                        pixelX,
                        pixelY,
                        dither);
                bool blend = semiTransparent && (texel & 0x8000) != 0;
                WriteDrawingPixel(pixelX, pixelY, source, blend, true);
            }
        }
    }

    private void DrawLine(IReadOnlyList<uint> packet)
    {
        uint commandWord = packet[0];
        bool gouraud = (commandWord & (1u << 28)) != 0;
        bool semiTransparent = (commandWord & (1u << 25)) != 0;
        var vertices = new List<RasterVertex>();

        if (gouraud)
        {
            RgbColor currentColor = DecodeRgb(commandWord);
            vertices.Add(CreateVertex(packet[1], currentColor, 0, 0));

            for (int packetIndex = 2; packetIndex + 1 < packet.Count; packetIndex += 2)
            {
                currentColor = DecodeRgb(packet[packetIndex]);
                vertices.Add(CreateVertex(packet[packetIndex + 1], currentColor, 0, 0));
            }
        }
        else
        {
            RgbColor color = DecodeRgb(commandWord);
            for (int packetIndex = 1; packetIndex < packet.Count; packetIndex++)
                vertices.Add(CreateVertex(packet[packetIndex], color, 0, 0));
        }

        for (int vertexIndex = 1; vertexIndex < vertices.Count; vertexIndex++)
        {
            DrawLineSegment(
                vertices[vertexIndex - 1],
                vertices[vertexIndex],
                semiTransparent);
        }
    }

    private void DrawLineSegment(
        RasterVertex start,
        RasterVertex end,
        bool semiTransparent)
    {
        int deltaX = Math.Abs(end.PixelX - start.PixelX);
        int deltaY = Math.Abs(end.PixelY - start.PixelY);
        int stepX = start.PixelX < end.PixelX ? 1 : -1;
        int stepY = start.PixelY < end.PixelY ? 1 : -1;
        int error = deltaX - deltaY;
        int pixelX = start.PixelX;
        int pixelY = start.PixelY;
        int totalSteps = Math.Max(deltaX, deltaY);
        int currentStep = 0;

        while (true)
        {
            double interpolation = totalSteps == 0
                ? 0
                : (double)currentStep / totalSteps;
            RgbColor color = InterpolateColor(start.Color, end.Color, interpolation);
            WriteDrawingPixel(
                pixelX,
                pixelY,
                PackColor(
                    color,
                    pixelX,
                    pixelY,
                    (_internalRegisters[0] & (1u << 9)) != 0),
                semiTransparent,
                false);

            if (pixelX == end.PixelX && pixelY == end.PixelY)
                break;

            int doubledError = error * 2;
            if (doubledError > -deltaY)
            {
                error -= deltaY;
                pixelX += stepX;
            }

            if (doubledError < deltaX)
            {
                error += deltaX;
                pixelY += stepY;
            }

            currentStep++;
        }
    }

    private void DrawRectangle(IReadOnlyList<uint> packet)
    {
        uint commandWord = packet[0];
        bool textured = (commandWord & (1u << 26)) != 0;
        bool semiTransparent = (commandWord & (1u << 25)) != 0;
        bool rawTexture = (commandWord & (1u << 24)) != 0;
        int sizeCode = (int)((commandWord >> 27) & 3);
        RasterVertex origin = CreateVertex(
            packet[1],
            DecodeRgb(commandWord),
            0,
            0);
        int packetIndex = 2;
        int textureU = 0;
        int textureV = 0;
        int colorLookupX = 0;
        int colorLookupY = 0;

        if (textured)
        {
            uint textureWord = packet[packetIndex++];
            textureU = (int)(textureWord & 0xFF);
            textureV = (int)((textureWord >> 8) & 0xFF);
            uint colorLookup = textureWord >> 16;
            colorLookupX = (int)(colorLookup & 0x3F) * 16;
            colorLookupY = (int)((colorLookup >> 6) & 0x1FF);
        }

        int width;
        int height;
        switch (sizeCode)
        {
            case 1:
                width = 1;
                height = 1;
                break;

            case 2:
                width = 8;
                height = 8;
                break;

            case 3:
                width = 16;
                height = 16;
                break;

            default:
                uint sizeWord = packet[packetIndex];
                width = (int)(sizeWord & 0x3FF);
                height = (int)((sizeWord >> 16) & 0x1FF);
                break;
        }

        uint texturePage = _internalRegisters[0] & 0x1FF;
        bool flipHorizontal = (_internalRegisters[0] & (1u << 12)) != 0;
        bool flipVertical = (_internalRegisters[0] & (1u << 13)) != 0;

        for (int offsetY = 0; offsetY < height; offsetY++)
        {
            for (int offsetX = 0; offsetX < width; offsetX++)
            {
                int pixelX = origin.PixelX + offsetX;
                int pixelY = origin.PixelY + offsetY;

                if (!textured)
                {
                    WriteDrawingPixel(
                        pixelX,
                        pixelY,
                        PackColor(origin.Color),
                        semiTransparent,
                        false);
                    continue;
                }

                int sampleU = textureU + (flipHorizontal ? width - 1 - offsetX : offsetX);
                int sampleV = textureV + (flipVertical ? height - 1 - offsetY : offsetY);
                if (!TrySampleTexture(
                        sampleU,
                        sampleV,
                        texturePage,
                        colorLookupX,
                        colorLookupY,
                        out ushort texel))
                {
                    continue;
                }

                ushort source = rawTexture
                    ? texel
                    : ModulateTexture(
                        texel,
                        origin.Color,
                        pixelX,
                        pixelY,
                        dither: false);
                bool blend = semiTransparent && (texel & 0x8000) != 0;
                WriteDrawingPixel(pixelX, pixelY, source, blend, true);
            }
        }
    }

    private bool TrySampleTexture(
        int textureU,
        int textureV,
        uint texturePage,
        int colorLookupX,
        int colorLookupY,
        out ushort texel)
    {
        ApplyTextureWindow(ref textureU, ref textureV);
        textureU &= 0xFF;
        textureV &= 0xFF;

        int pageX = (int)(texturePage & 0x0F) * 64;
        int pageY = (int)((texturePage >> 4) & 1) * 256;
        int textureDepth = (int)((texturePage >> 7) & 3);

        switch (textureDepth)
        {
            case 0:
                {
                    ushort packed = Vram.ReadPixel(
                        pageX + textureU / 4,
                        pageY + textureV);
                    int colorIndex =
                        (packed >> ((textureU & 3) * 4)) & 0x0F;
                    texel = Vram.ReadPixel(
                        colorLookupX + colorIndex,
                        colorLookupY);
                    break;
                }

            case 1:
                {
                    ushort packed = Vram.ReadPixel(
                        pageX + textureU / 2,
                        pageY + textureV);
                    int colorIndex =
                        (packed >> ((textureU & 1) * 8)) & 0xFF;
                    texel = Vram.ReadPixel(
                        colorLookupX + colorIndex,
                        colorLookupY);
                    break;
                }

            default:
                texel = Vram.ReadPixel(pageX + textureU, pageY + textureV);
                break;
        }

        return texel != 0;
    }

    private void ApplyTextureWindow(ref int textureU, ref int textureV)
    {
        uint textureWindow = _internalRegisters[2];
        int maskX = (int)(textureWindow & 0x1F);
        int maskY = (int)((textureWindow >> 5) & 0x1F);
        int offsetX = (int)((textureWindow >> 10) & 0x1F);
        int offsetY = (int)((textureWindow >> 15) & 0x1F);

        textureU =
            (textureU & ~(maskX * 8)) |
            ((offsetX & maskX) * 8);
        textureV =
            (textureV & ~(maskY * 8)) |
            ((offsetY & maskY) * 8);
    }

    private static ushort ModulateTexture(
        ushort texel,
        RgbColor color,
        int pixelX,
        int pixelY,
        bool dither)
    {
        var modulated = new RgbColor(
            Math.Min(255, (texel & 0x1F) * color.Red / 16),
            Math.Min(255, ((texel >> 5) & 0x1F) * color.Green / 16),
            Math.Min(255, ((texel >> 10) & 0x1F) * color.Blue / 16));
        return (ushort)(
            PackColor(modulated, pixelX, pixelY, dither) |
            (texel & 0x8000));
    }

    private void WriteDrawingPixel(
        int pixelX,
        int pixelY,
        ushort source,
        bool semiTransparent,
        bool preserveSourceMask)
    {
        GetDrawingArea(
            out int drawingLeft,
            out int drawingTop,
            out int drawingRight,
            out int drawingBottom);

        if (pixelX < drawingLeft ||
            pixelX > drawingRight ||
            pixelY < drawingTop ||
            pixelY > drawingBottom ||
            pixelX < 0 ||
            pixelX >= Vram.Width ||
            pixelY < 0 ||
            pixelY >= Vram.Height)
        {
            return;
        }

        ushort destination = Vram.ReadPixel(pixelX, pixelY);
        if ((_internalRegisters[6] & 2) != 0 && (destination & 0x8000) != 0)
            return;

        ushort result = semiTransparent
            ? BlendColors(destination, source)
            : (ushort)(source & 0x7FFF);

        if (preserveSourceMask && (source & 0x8000) != 0)
            result |= 0x8000;

        if ((_internalRegisters[6] & 1) != 0)
            result |= 0x8000;

        Vram.WritePixel(pixelX, pixelY, result);
    }

    private ushort BlendColors(ushort background, ushort foreground)
    {
        int backgroundRed = background & 0x1F;
        int backgroundGreen = (background >> 5) & 0x1F;
        int backgroundBlue = (background >> 10) & 0x1F;
        int foregroundRed = foreground & 0x1F;
        int foregroundGreen = (foreground >> 5) & 0x1F;
        int foregroundBlue = (foreground >> 10) & 0x1F;
        int mode = (int)((_internalRegisters[0] >> 5) & 3);

        int red = BlendChannel(backgroundRed, foregroundRed, mode);
        int green = BlendChannel(backgroundGreen, foregroundGreen, mode);
        int blue = BlendChannel(backgroundBlue, foregroundBlue, mode);
        return (ushort)(red | (green << 5) | (blue << 10));
    }

    private static int BlendChannel(int background, int foreground, int mode)
    {
        int result = mode switch
        {
            0 => background / 2 + foreground / 2,
            1 => background + foreground,
            2 => background - foreground,
            3 => background + foreground / 4,
            _ => foreground,
        };

        return Math.Clamp(result, 0, 31);
    }

    private RasterVertex CreateVertex(
        uint coordinateWord,
        RgbColor color,
        int textureU,
        int textureV)
    {
        int offsetX = SignExtend11((int)(_internalRegisters[5] & 0x7FF));
        int offsetY = SignExtend11((int)((_internalRegisters[5] >> 11) & 0x7FF));
        int pixelX = SignExtend11((int)(coordinateWord & 0x7FF)) + offsetX;
        int pixelY = SignExtend11((int)((coordinateWord >> 16) & 0x7FF)) + offsetY;
        return new RasterVertex(pixelX, pixelY, color, textureU, textureV);
    }

    private void GetDrawingArea(
        out int left,
        out int top,
        out int right,
        out int bottom)
    {
        left = (int)(_internalRegisters[3] & 0x3FF);
        top = (int)((_internalRegisters[3] >> 10) & 0x1FF);
        right = (int)(_internalRegisters[4] & 0x3FF);
        bottom = (int)((_internalRegisters[4] >> 10) & 0x1FF);
    }

    private static int SignExtend11(int value)
    {
        return (value & 0x400) != 0 ? value - 0x800 : value;
    }

    private static RgbColor DecodeRgb(uint value)
    {
        return new RgbColor(
            (int)(value & 0xFF),
            (int)((value >> 8) & 0xFF),
            (int)((value >> 16) & 0xFF));
    }

    private static ushort PackColor(RgbColor color) =>
        PackColor(color, 0, 0, dither: false);

    private static ushort PackColor(
        RgbColor color,
        int pixelX,
        int pixelY,
        bool dither)
    {
        int ditherOffset = dither
            ? DitherMatrix[pixelY & 3, pixelX & 3]
            : 0;
        int red = Math.Clamp(color.Red + ditherOffset, 0, 255) >> 3;
        int green = Math.Clamp(color.Green + ditherOffset, 0, 255) >> 3;
        int blue = Math.Clamp(color.Blue + ditherOffset, 0, 255) >> 3;
        return (ushort)(red | (green << 5) | (blue << 10));
    }

    private static bool IsInsideTopLeft(
        double edgeValue,
        RasterVertex start,
        RasterVertex end)
    {
        if (edgeValue > 0)
            return true;
        if (edgeValue < 0)
            return false;

        int deltaX = end.PixelX - start.PixelX;
        int deltaY = end.PixelY - start.PixelY;
        return deltaY > 0 || (deltaY == 0 && deltaX < 0);
    }

    private static RgbColor InterpolateColor(
        RasterVertex first,
        RasterVertex second,
        RasterVertex third,
        double firstWeight,
        double secondWeight,
        double thirdWeight)
    {
        return new RgbColor(
            (int)Math.Round(
                first.Color.Red * firstWeight +
                second.Color.Red * secondWeight +
                third.Color.Red * thirdWeight),
            (int)Math.Round(
                first.Color.Green * firstWeight +
                second.Color.Green * secondWeight +
                third.Color.Green * thirdWeight),
            (int)Math.Round(
                first.Color.Blue * firstWeight +
                second.Color.Blue * secondWeight +
                third.Color.Blue * thirdWeight));
    }

    private static RgbColor InterpolateColor(
        RgbColor start,
        RgbColor end,
        double interpolation)
    {
        return new RgbColor(
            (int)Math.Round(start.Red + (end.Red - start.Red) * interpolation),
            (int)Math.Round(start.Green + (end.Green - start.Green) * interpolation),
            (int)Math.Round(start.Blue + (end.Blue - start.Blue) * interpolation));
    }

    private static double Edge(
        RasterVertex start,
        RasterVertex end,
        double pixelX,
        double pixelY)
    {
        return (pixelX - start.PixelX) * (end.PixelY - start.PixelY) -
               (pixelY - start.PixelY) * (end.PixelX - start.PixelX);
    }
}
