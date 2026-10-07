using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Text;

/// <summary>
/// Writes a one-sheet Excel report (.xlsx): title, a Field / Value table and an optional JPG picture below it.
/// No plugin and no compression library (an .xlsx is a zip of XML files; it is written "stored"), so it works
/// the same in the Editor and in WebGL. A report is a few KB of XML plus the picture.
/// </summary>
public static class XlsxReport
{
    public enum Highlight { None, Good, Warning, Critical }

    public class Row
    {
        public string label, value;
        public Highlight highlight;
    }

    // Style indexes in styles.xml (cellXfs).
    private const int StyleTitle = 1, StyleSubtitle = 2, StyleNote = 3, StyleHeader = 4, StyleLabel = 5, StyleValue = 6,
        StyleGood = 7, StyleWarning = 8, StyleCritical = 9;

    public static byte[] Build(string title, string subtitle, string note, IList<Row> rows, byte[] jpg, int imageWidth, int imageHeight)
    {
        bool hasImage = jpg != null && jpg.Length > 0;
        var files = new List<KeyValuePair<string, byte[]>>
        {
            Part("[Content_Types].xml", ContentTypes(hasImage)),
            Part("_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                "</Relationships>"),
            Part("xl/workbook.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                "<sheets><sheet name=\"Maintenance Report\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>"),
            Part("xl/_rels/workbook.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
                "</Relationships>"),
            Part("xl/styles.xml", Styles()),
        };

        int firstRow = 6, lastRow = firstRow + rows.Count - 1;
        int imageTitleRow = lastRow + 2; // one empty row, then "Part Image"
        files.Add(Part("xl/worksheets/sheet1.xml", Sheet(title, subtitle, note, rows, firstRow, hasImage ? imageTitleRow : -1)));

        if (hasImage)
        {
            files.Add(Part("xl/worksheets/_rels/sheet1.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing\" Target=\"../drawings/drawing1.xml\"/>" +
                "</Relationships>"));
            files.Add(Part("xl/drawings/drawing1.xml", Drawing(imageTitleRow /* 0-based row under the title */, imageWidth, imageHeight)));
            files.Add(Part("xl/drawings/_rels/drawing1.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"../media/image1.jpeg\"/>" +
                "</Relationships>"));
            files.Add(new KeyValuePair<string, byte[]>("xl/media/image1.jpeg", jpg));
        }

        return StoredZip(files);
    }

    // ------------------------------------------------------------------ parts

    private static string ContentTypes(bool hasImage)
    {
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
               "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
               "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
               "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
               (hasImage ? "<Default Extension=\"jpeg\" ContentType=\"image/jpeg\"/>" : "") +
               "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
               "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
               "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
               (hasImage ? "<Override PartName=\"/xl/drawings/drawing1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.drawing+xml\"/>" : "") +
               "</Types>";
    }

    private static string Styles()
    {
        const string border = "<border><left style=\"thin\"><color rgb=\"FFC3C9D6\"/></left><right style=\"thin\"><color rgb=\"FFC3C9D6\"/></right>" +
                              "<top style=\"thin\"><color rgb=\"FFC3C9D6\"/></top><bottom style=\"thin\"><color rgb=\"FFC3C9D6\"/></bottom><diagonal/></border>";
        const string wrapTop = "<alignment vertical=\"top\" wrapText=\"1\"/>";
        const string center = "<alignment vertical=\"center\"/>";

        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
               "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
               "<fonts count=\"5\">" +
               "<font><sz val=\"11\"/><color rgb=\"FF1F2433\"/><name val=\"Calibri\"/><family val=\"2\"/></font>" +           // 0 normal
               "<font><b/><sz val=\"11\"/><color rgb=\"FF1F2433\"/><name val=\"Calibri\"/><family val=\"2\"/></font>" +       // 1 bold
               "<font><b/><sz val=\"18\"/><color rgb=\"FF1F2A44\"/><name val=\"Calibri\"/><family val=\"2\"/></font>" +       // 2 title
               "<font><i/><sz val=\"10\"/><color rgb=\"FF6B7385\"/><name val=\"Calibri\"/><family val=\"2\"/></font>" +       // 3 note
               "<font><b/><sz val=\"11\"/><color rgb=\"FFFFFFFF\"/><name val=\"Calibri\"/><family val=\"2\"/></font>" +       // 4 header
               "</fonts>" +
               "<fills count=\"7\">" +
               "<fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill>" +
               Fill("FF1F2A44") + Fill("FFEEF1F6") + Fill("FFD9F2E1") + Fill("FFFFF1CC") + Fill("FFFADBD8") +          // 2 header, 3 label, 4-6 condition
               "</fills>" +
               "<borders count=\"2\"><border><left/><right/><top/><bottom/><diagonal/></border>" + border + "</borders>" +
               "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
               "<cellXfs count=\"10\">" +
               "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
               Xf(2, 0, 0, center) + Xf(1, 0, 0, center) + Xf(3, 0, 0, center) +   // 1 title, 2 subtitle, 3 note
               Xf(4, 2, 1, center) +                                                // 4 header
               Xf(1, 3, 1, wrapTop) + Xf(0, 0, 1, wrapTop) +                       // 5 label, 6 value
               Xf(1, 4, 1, wrapTop) + Xf(1, 5, 1, wrapTop) + Xf(1, 6, 1, wrapTop) + // 7 good, 8 warning, 9 critical
               "</cellXfs>" +
               "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
               "</styleSheet>";
    }

    private static string Fill(string argb) => $"<fill><patternFill patternType=\"solid\"><fgColor rgb=\"{argb}\"/><bgColor indexed=\"64\"/></patternFill></fill>";

    private static string Xf(int font, int fill, int border, string alignment) =>
        $"<xf numFmtId=\"0\" fontId=\"{font}\" fillId=\"{fill}\" borderId=\"{border}\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\">{alignment}</xf>";

    private static string Sheet(string title, string subtitle, string note, IList<Row> rows, int firstRow, int imageTitleRow)
    {
        var sb = new StringBuilder(4096);
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
        sb.Append("<sheetPr><pageSetUpPr fitToPage=\"1\"/></sheetPr>");
        sb.Append("<sheetViews><sheetView workbookViewId=\"0\" showGridLines=\"0\"/></sheetViews>");
        sb.Append("<sheetFormatPr defaultRowHeight=\"15\"/>");
        sb.Append("<cols><col min=\"1\" max=\"1\" width=\"28\" customWidth=\"1\"/><col min=\"2\" max=\"2\" width=\"64\" customWidth=\"1\"/></cols>");
        sb.Append("<sheetData>");

        RowXml(sb, 1, 30, Cell("A1", title, StyleTitle));
        RowXml(sb, 2, 20, Cell("A2", subtitle, StyleSubtitle));
        RowXml(sb, 3, 16, Cell("A3", note, StyleNote));
        RowXml(sb, 5, 20, Cell("A5", "Field", StyleHeader) + Cell("B5", "Value", StyleHeader));

        for (int i = 0; i < rows.Count; i++)
        {
            int r = firstRow + i;
            int valueStyle = rows[i].highlight == Highlight.Good ? StyleGood
                : rows[i].highlight == Highlight.Warning ? StyleWarning
                : rows[i].highlight == Highlight.Critical ? StyleCritical : StyleValue;
            RowXml(sb, r, RowHeight(rows[i].value), Cell("A" + r, rows[i].label, StyleLabel) + Cell("B" + r, rows[i].value, valueStyle));
        }

        if (imageTitleRow > 0)
            RowXml(sb, imageTitleRow, 20, Cell("A" + imageTitleRow, "Part Image", StyleHeader) + Cell("B" + imageTitleRow, "", StyleHeader));

        sb.Append("</sheetData>");
        sb.Append("<mergeCells count=\"").Append(imageTitleRow > 0 ? 4 : 3).Append("\">");
        sb.Append("<mergeCell ref=\"A1:B1\"/><mergeCell ref=\"A2:B2\"/><mergeCell ref=\"A3:B3\"/>");
        if (imageTitleRow > 0) sb.Append("<mergeCell ref=\"A").Append(imageTitleRow).Append(":B").Append(imageTitleRow).Append("\"/>");
        sb.Append("</mergeCells>");
        sb.Append("<pageMargins left=\"0.5\" right=\"0.5\" top=\"0.6\" bottom=\"0.6\" header=\"0.3\" footer=\"0.3\"/>");
        sb.Append("<pageSetup paperSize=\"9\" orientation=\"portrait\" fitToWidth=\"1\" fitToHeight=\"0\"/>");
        if (imageTitleRow > 0) sb.Append("<drawing r:id=\"rId1\"/>");
        sb.Append("</worksheet>");
        return sb.ToString();
    }

    // Explicit height for wrapped text (~60 characters per line in the 64-wide column), so the picture below
    // lands in the same place in Excel, LibreOffice and Google Sheets (they auto-fit rows differently).
    private static float RowHeight(string text)
    {
        int lines = 0;
        foreach (string part in (text ?? string.Empty).Split('\n'))
            lines += Math.Max(1, (part.Length + 59) / 60);
        return Math.Max(18f, lines * 15f + 4f);
    }

    private static void RowXml(StringBuilder sb, int r, float height, string cells)
    {
        sb.Append("<row r=\"").Append(r).Append('"');
        if (height > 0) sb.Append(" ht=\"").Append(height.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append("\" customHeight=\"1\"");
        sb.Append('>').Append(cells).Append("</row>");
    }

    private static string Cell(string reference, string text, int style) =>
        $"<c r=\"{reference}\" s=\"{style}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{Escape(text)}</t></is></c>";

    // Picture anchored at column A of the 0-based row (= the row under "Part Image"), at its own size.
    private static string Drawing(int row0, int widthPx, int heightPx)
    {
        long cx = widthPx * 9525L, cy = heightPx * 9525L; // pixels -> EMU
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
               "<xdr:wsDr xmlns:xdr=\"http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
               "<xdr:oneCellAnchor>" +
               $"<xdr:from><xdr:col>0</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>{row0}</xdr:row><xdr:rowOff>95250</xdr:rowOff></xdr:from>" +
               $"<xdr:ext cx=\"{cx}\" cy=\"{cy}\"/>" +
               "<xdr:pic><xdr:nvPicPr><xdr:cNvPr id=\"2\" name=\"Part Image\"/><xdr:cNvPicPr><a:picLocks noChangeAspect=\"1\"/></xdr:cNvPicPr></xdr:nvPicPr>" +
               "<xdr:blipFill><a:blip r:embed=\"rId1\"/><a:stretch><a:fillRect/></a:stretch></xdr:blipFill>" +
               $"<xdr:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"{cx}\" cy=\"{cy}\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></xdr:spPr>" +
               "</xdr:pic><xdr:clientData/></xdr:oneCellAnchor></xdr:wsDr>";
    }

    // XML-safe text (also drops control characters Excel refuses).
    private static string Escape(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
            if (c == '\t' || c == '\n' || c == '\r' || c >= 0x20) sb.Append(c);
        return SecurityElement.Escape(sb.ToString());
    }

    private static KeyValuePair<string, byte[]> Part(string path, string xml) =>
        new KeyValuePair<string, byte[]>(path, new UTF8Encoding(false).GetBytes(xml));

    // ------------------------------------------------------------------ zip (stored, no compression)

    private static byte[] StoredZip(List<KeyValuePair<string, byte[]>> files)
    {
        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            DateTime now = DateTime.Now;
            ushort dosTime = (ushort)((now.Hour << 11) | (now.Minute << 5) | (now.Second / 2));
            ushort dosDate = (ushort)(((now.Year - 1980) << 9) | (now.Month << 5) | now.Day);

            var offsets = new uint[files.Count];
            var crcs = new uint[files.Count];
            for (int i = 0; i < files.Count; i++)
            {
                byte[] name = Encoding.ASCII.GetBytes(files[i].Key);
                byte[] data = files[i].Value;
                offsets[i] = (uint)ms.Position;
                crcs[i] = Crc32(data);

                w.Write(0x04034b50u); w.Write((ushort)20); w.Write((ushort)0); w.Write((ushort)0);
                w.Write(dosTime); w.Write(dosDate); w.Write(crcs[i]);
                w.Write((uint)data.Length); w.Write((uint)data.Length);
                w.Write((ushort)name.Length); w.Write((ushort)0);
                w.Write(name); w.Write(data);
            }

            uint centralStart = (uint)ms.Position;
            for (int i = 0; i < files.Count; i++)
            {
                byte[] name = Encoding.ASCII.GetBytes(files[i].Key);
                uint size = (uint)files[i].Value.Length;
                w.Write(0x02014b50u); w.Write((ushort)20); w.Write((ushort)20); w.Write((ushort)0); w.Write((ushort)0);
                w.Write(dosTime); w.Write(dosDate); w.Write(crcs[i]); w.Write(size); w.Write(size);
                w.Write((ushort)name.Length); w.Write((ushort)0); w.Write((ushort)0);
                w.Write((ushort)0); w.Write((ushort)0); w.Write(0u); w.Write(offsets[i]);
                w.Write(name);
            }
            uint centralSize = (uint)ms.Position - centralStart;

            w.Write(0x06054b50u); w.Write((ushort)0); w.Write((ushort)0);
            w.Write((ushort)files.Count); w.Write((ushort)files.Count);
            w.Write(centralSize); w.Write(centralStart); w.Write((ushort)0);
            w.Flush();
            return ms.ToArray();
        }
    }

    private static uint[] crcTable;

    private static uint Crc32(byte[] data)
    {
        if (crcTable == null)
        {
            crcTable = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                crcTable[n] = c;
            }
        }
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in data) crc = crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }
}
