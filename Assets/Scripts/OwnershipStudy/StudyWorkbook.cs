using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace OwnershipStudy
{
    public sealed class StudyWorkbook
    {
        private const string Spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private const string OfficeRelationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private const string PackageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";
        private const string ContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";
        private static readonly string[] SheetNames = { "Session", "Trials", "Attempts" };

        private readonly List<object[]>[] sheets =
        {
            new List<object[]>(),
            new List<object[]>(),
            new List<object[]>()
        };

        public List<object[]> Session => sheets[0];
        public List<object[]> Trials => sheets[1];
        public List<object[]> Attempts => sheets[2];

        public void Save(string path)
        {
            string temporary = path + ".tmp";
            using (var archive = new ZipArchive(File.Create(temporary), ZipArchiveMode.Create))
            {
                Write(archive, "[Content_Types].xml", xml =>
                {
                    xml.WriteStartElement("Types", ContentTypes);
                    Element(xml, "Default", "Extension", "rels", "ContentType", "application/vnd.openxmlformats-package.relationships+xml");
                    Element(xml, "Default", "Extension", "xml", "ContentType", "application/xml");
                    Element(xml, "Override", "PartName", "/xl/workbook.xml", "ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
                    for (int i = 1; i <= sheets.Length; i++)
                        Element(xml, "Override", "PartName", $"/xl/worksheets/sheet{i}.xml", "ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
                    xml.WriteEndElement();
                });
                Write(archive, "_rels/.rels", xml =>
                {
                    xml.WriteStartElement("Relationships", PackageRelationships);
                    Relationship(xml, 1, "officeDocument", "xl/workbook.xml");
                    xml.WriteEndElement();
                });
                Write(archive, "xl/workbook.xml", xml =>
                {
                    xml.WriteStartElement("workbook", Spreadsheet);
                    xml.WriteStartElement("sheets");
                    for (int i = 0; i < sheets.Length; i++)
                    {
                        string id = (i + 1).ToString(CultureInfo.InvariantCulture);
                        xml.WriteStartElement("sheet");
                        xml.WriteAttributeString("name", SheetNames[i]);
                        xml.WriteAttributeString("sheetId", id);
                        xml.WriteAttributeString("r", "id", OfficeRelationships, "rId" + id);
                        xml.WriteEndElement();
                    }
                    xml.WriteEndElement();
                    xml.WriteEndElement();
                });
                Write(archive, "xl/_rels/workbook.xml.rels", xml =>
                {
                    xml.WriteStartElement("Relationships", PackageRelationships);
                    for (int i = 1; i <= sheets.Length; i++)
                        Relationship(xml, i, "worksheet", $"worksheets/sheet{i}.xml");
                    xml.WriteEndElement();
                });
                for (int i = 0; i < sheets.Length; i++)
                {
                    List<object[]> rows = sheets[i];
                    Write(archive, $"xl/worksheets/sheet{i + 1}.xml", xml => WriteSheet(xml, rows));
                }
            }
            if (File.Exists(path))
                File.Replace(temporary, path, null);
            else
                File.Move(temporary, path);
        }

        private static void WriteSheet(XmlWriter xml, List<object[]> rows)
        {
            xml.WriteStartElement("worksheet", Spreadsheet);
            xml.WriteStartElement("sheetData");
            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                xml.WriteStartElement("row");
                object[] row = rows[rowIndex];
                for (int i = 0; i < row.Length; i++)
                {
                    if (row[i] == null)
                        continue;
                    xml.WriteStartElement("c");
                    xml.WriteAttributeString("r", Column(i) + (rowIndex + 1).ToString(CultureInfo.InvariantCulture));
                    if (row[i] is string text)
                    {
                        xml.WriteAttributeString("t", "inlineStr");
                        xml.WriteStartElement("is");
                        xml.WriteElementString("t", text);
                        xml.WriteEndElement();
                    }
                    else
                    {
                        xml.WriteElementString("v", Convert.ToString(row[i], CultureInfo.InvariantCulture));
                    }
                    xml.WriteEndElement();
                }
                xml.WriteEndElement();
            }
            xml.WriteEndElement();
            xml.WriteEndElement();
        }

        private static string Column(int index)
        {
            string name = "";
            for (int value = index + 1; value > 0; value = (value - 1) / 26)
                name = (char)('A' + (value - 1) % 26) + name;
            return name;
        }

        private static void Relationship(XmlWriter xml, int id, string type, string target)
        {
            Element(xml, "Relationship", "Id", "rId" + id, "Type", OfficeRelationships + "/" + type, "Target", target);
        }

        private static void Element(XmlWriter xml, string name, params string[] attributes)
        {
            xml.WriteStartElement(name);
            for (int i = 0; i < attributes.Length; i += 2)
                xml.WriteAttributeString(attributes[i], attributes[i + 1]);
            xml.WriteEndElement();
        }

        private static void Write(ZipArchive archive, string name, Action<XmlWriter> content)
        {
            using (var stream = archive.CreateEntry(name).Open())
            using (var xml = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false) }))
                content(xml);
        }
    }
}
