using System.Text;
using System.Xml;
using System.Xml.Xsl;
namespace Nashi.Core;
/// <summary>
/// Turkish e-Fatura, e-Arşiv and e-İrsaliye files (UBL-TR XML) carry their own view: an XSLT
/// stylesheet embedded as base64. Applying it gives the page GİB's portal and accounting apps
/// show, which the HTML to PDF route then prints. The stylesheet runs without scripts in XSLT,
/// without document(), includes, DTDs or any resolver, so it reads nothing but the invoice itself.
/// </summary>
public static class EInvoice
{
    public const string NoView="This e-invoice has no embedded view (XSLT) to print.";
    public const string Unreadable="This e-invoice view could not be applied. The XML or its embedded XSLT is damaged or uses features Nashi does not run.";
    public const long MaximumSize=25_000_000;
    // The UBL 2.1 documents GİB uses for invoices, credit notes and despatch advice.
    private static readonly string[] Documents=["Invoice","CreditNote","DespatchAdvice","ReceiptAdvice"];
    private const string Cac="urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2",Cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";

    private static XmlReaderSettings Secure=>new(){DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,IgnoreComments=true,MaxCharactersInDocument=MaximumSize*2,MaxCharactersFromEntities=0};

    /// <summary>True for a UBL e-invoice or despatch advice; reads only up to the root element.</summary>
    public static bool IsInvoice(string path)
    {
        if(!string.Equals(Path.GetExtension(path),".xml",StringComparison.OrdinalIgnoreCase)||!File.Exists(path))return false;
        try
        {
            if(new FileInfo(path).Length>MaximumSize)return false;
            using var reader=XmlReader.Create(path,Secure);reader.MoveToContent();
            return reader.NodeType==XmlNodeType.Element&&Documents.Contains(reader.LocalName)&&reader.NamespaceURI.StartsWith("urn:oasis:names:specification:ubl:schema:xsd:",StringComparison.Ordinal);
        }
        catch(Exception error)when(error is XmlException or IOException or UnauthorizedAccessException){return false;}
    }

    /// <summary>The embedded XSLT, decoded; null when the document has none.</summary>
    public static string? Stylesheet(XmlDocument invoice)
    {
        var names=new XmlNamespaceManager(invoice.NameTable);names.AddNamespace("cac",Cac);names.AddNamespace("cbc",Cbc);
        foreach(XmlElement reference in invoice.SelectNodes("/*/cac:AdditionalDocumentReference",names)!)
        {
            if(reference.SelectSingleNode("cac:Attachment/cbc:EmbeddedDocumentBinaryObject",names) is not XmlElement binary)continue;
            var name=binary.GetAttribute("filename");var type=reference.SelectSingleNode("cbc:DocumentType",names)?.InnerText??"";
            byte[] bytes;try{bytes=Convert.FromBase64String(binary.InnerText.Trim());}catch(FormatException){continue;}
            var text=Decode(bytes);
            // GİB names the view "*.xslt" and/or types it "XSLT"; the content must be a stylesheet either way.
            if((name.EndsWith(".xslt",StringComparison.OrdinalIgnoreCase)||name.EndsWith(".xsl",StringComparison.OrdinalIgnoreCase)||type.Equals("XSLT",StringComparison.OrdinalIgnoreCase)||text.Contains("http://www.w3.org/1999/XSL/Transform",StringComparison.Ordinal))&&text.Contains("http://www.w3.org/1999/XSL/Transform",StringComparison.Ordinal))return text;
        }
        return null;
    }

    /// <summary>Applies the invoice's own view and returns the HTML page.</summary>
    public static string ToHtml(string path,CancellationToken token=default)
    {
        if(new FileInfo(path).Length>MaximumSize)throw new InvalidDataException(Unreadable);
        var invoice=new XmlDocument{XmlResolver=null,PreserveWhitespace=true};
        try{using var reader=XmlReader.Create(path,Secure);invoice.Load(reader);}catch(XmlException){throw new InvalidDataException(Unreadable);}
        token.ThrowIfCancellationRequested();
        var stylesheet=Stylesheet(invoice)??throw new InvalidDataException(NoView);
        var transform=new XslCompiledTransform();
        try
        {
            using(var reader=XmlReader.Create(new StringReader(stylesheet),Secure))transform.Load(reader,new XsltSettings(enableDocumentFunction:false,enableScript:false),null);
            token.ThrowIfCancellationRequested();
            // UTF-8 output, so an HTML view declares the charset the file is written in.
            using var output=new MemoryStream();var settings=transform.OutputSettings!.Clone();settings.ConformanceLevel=ConformanceLevel.Auto;settings.Encoding=new UTF8Encoding(false);
            using(var writer=XmlWriter.Create(output,settings))using(var input=new XmlNodeReader(invoice))transform.Transform(input,null,writer,null);
            var html=new UTF8Encoding(false).GetString(output.ToArray());
            // The page is printed from its own folder, so a charset must be declared for Turkish text.
            return html.Contains("charset",StringComparison.OrdinalIgnoreCase)?html:"<meta charset=\"utf-8\">"+html;
        }
        catch(Exception error)when(error is XsltException or XmlException or InvalidOperationException or ArgumentException){throw new InvalidDataException(Unreadable);}
    }

    private static string Decode(byte[] bytes)
    {
        // UTF-8 (with or without BOM) is the norm; UTF-16 views exist too.
        if(bytes.Length>=2&&(bytes[0]==0xFF&&bytes[1]==0xFE||bytes[0]==0xFE&&bytes[1]==0xFF))return new UnicodeEncoding(bytes[0]==0xFE,true).GetString(bytes);
        return new UTF8Encoding(false).GetString(bytes).TrimStart('﻿');
    }
}
