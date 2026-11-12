using System.Text;
using Nashi.Core;
internal static class EInvoiceChecks
{
    internal const string Ubl="urn:oasis:names:specification:ubl:schema:xsd:";
    /// <summary>A synthetic UBL-TR e-Arşiv invoice whose embedded view draws a code with a script.</summary>
    internal static string Invoice(string? stylesheet,string root="Invoice")=>
        $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><{root} xmlns=\"{Ubl}{root}-2\" xmlns:cac=\"{Ubl}CommonAggregateComponents-2\" xmlns:cbc=\"{Ubl}CommonBasicComponents-2\">"+
        "<cbc:UBLVersionID>2.1</cbc:UBLVersionID><cbc:CustomizationID>TR1.2</cbc:CustomizationID><cbc:ProfileID>EARSIVFATURA</cbc:ProfileID><cbc:ID>NSH2026000000417</cbc:ID>"+
        (stylesheet is null?"":"<cac:AdditionalDocumentReference><cbc:ID>1</cbc:ID><cbc:DocumentType>XSLT</cbc:DocumentType><cac:Attachment><cbc:EmbeddedDocumentBinaryObject characterSetCode=\"UTF-8\" encodingCode=\"Base64\" filename=\"NSH2026000000417.xslt\" mimeCode=\"application/xml\">"+Convert.ToBase64String(Encoding.UTF8.GetBytes(stylesheet))+"</cbc:EmbeddedDocumentBinaryObject></cac:Attachment></cac:AdditionalDocumentReference>")+
        "<cac:AccountingCustomerParty><cac:Party><cac:PartyName><cbc:Name>Çağrı Öztürk Ltd. Şti.</cbc:Name></cac:PartyName></cac:Party></cac:AccountingCustomerParty>"+
        $"<cac:LegalMonetaryTotal><cbc:PayableAmount currencyID=\"TRY\">1250.00</cbc:PayableAmount></cac:LegalMonetaryTotal></{root}>";
    internal static string View(string body="")=>
        $"<xsl:stylesheet version=\"1.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" xmlns:inv=\"{Ubl}Invoice-2\" xmlns:cac=\"{Ubl}CommonAggregateComponents-2\" xmlns:cbc=\"{Ubl}CommonBasicComponents-2\">"+
        "<xsl:output method=\"html\" encoding=\"UTF-8\"/><xsl:template match=\"/\"><html><head><title>e-Arşiv Fatura</title></head><body>"+
        "<h1>Fatura No: <xsl:value-of select=\"/inv:Invoice/cbc:ID\"/></h1><p>Alıcı: <xsl:value-of select=\"//cac:AccountingCustomerParty//cbc:Name\"/></p><p>Ödenecek: <xsl:value-of select=\"//cbc:PayableAmount\"/> TRY</p>"+body+
        "<canvas id=\"qr\" width=\"120\" height=\"120\"></canvas><script>var c=document.getElementById('qr').getContext('2d');c.fillRect(0,0,120,120);var p=document.createElement('p');p.textContent='QR drawn';document.body.appendChild(p);</script>"+
        "</body></html></xsl:template></xsl:stylesheet>";

    internal static void Run(Action<bool,string> check,string root)
    {
        var folder=Path.Combine(root,"E-invoices");Directory.CreateDirectory(folder);
        try
        {
            string Write(string name,string text){var path=Path.Combine(folder,name);File.WriteAllText(path,text,new UTF8Encoding(false));return path;}
            var invoice=Write("NSH2026000000417.xml",Invoice(View()));
            check(EInvoice.IsInvoice(invoice)&&EInvoice.IsInvoice(Write("Irsaliye.xml",Invoice(View(),"DespatchAdvice"))),"UBL invoices and despatch advice must be recognised");
            check(!EInvoice.IsInvoice(Write("Feed.xml","<rss><channel/></rss>"))&&!EInvoice.IsInvoice(Write("Invoice.txt",Invoice(View())))&&!EInvoice.IsInvoice(Path.Combine(folder,"Missing.xml")),"Other XML, other extensions and missing files are not invoices");
            check(!EInvoice.IsInvoice(Write("Dtd.xml","<?xml version=\"1.0\"?><!DOCTYPE Invoice [<!ENTITY x SYSTEM \"file:///C:/Windows/win.ini\">]><Invoice xmlns=\""+Ubl+"Invoice-2\">&x;</Invoice>")),"A DTD must be refused, not resolved");
            check(FeatureCatalog.Formats([invoice]).SequenceEqual(["html","pdf"])&&FeatureCatalog.Formats([invoice,Write("Plain.pdf","%PDF-1.4")]).Length==0,"An e-invoice converts to HTML and PDF, not mixed with other files");

            var html=EInvoice.ToHtml(invoice);
            check(html.Contains("NSH2026000000417")&&html.Contains("Çağrı Öztürk Ltd. Şti.")&&html.Contains("1250.00 TRY"),"The embedded view must show the invoice's own values: "+html[..Math.Min(300,html.Length)]);
            check(html.Contains("<script>")&&html.Contains("<canvas")&&html.Contains("charset",StringComparison.OrdinalIgnoreCase)&&!html.Contains("utf-16",StringComparison.OrdinalIgnoreCase),"The page must keep its script-drawn code and declare UTF-8");

            void Refused(string name,string text,string message){try{EInvoice.ToHtml(Write(name,text));check(false,name+" was printed");}catch(InvalidDataException error){check(error.Message==message,name+": "+error.Message);}}
            Refused("NoView.xml",Invoice(null),EInvoice.NoView);
            Refused("Damaged.xml",Invoice(View()).Replace("</Invoice>",""),EInvoice.Unreadable);
            Refused("BadBase64.xml",Invoice(View()).Replace("encodingCode=\"Base64\" filename=\"NSH2026000000417.xslt\" mimeCode=\"application/xml\">","encodingCode=\"Base64\" filename=\"NSH2026000000417.xslt\" mimeCode=\"application/xml\">@@@"),EInvoice.NoView);
            var secret=Write("secret.txt","TOP SECRET");
            Refused("ReadsFile.xml",Invoice(View("<pre><xsl:value-of select=\"document('"+new Uri(secret).AbsoluteUri+"')\"/></pre>")),EInvoice.Unreadable);
            Refused("Includes.xml",Invoice(View().Replace("<xsl:output","<xsl:include href=\""+new Uri(secret).AbsoluteUri+"\"/><xsl:output")),EInvoice.Unreadable);
            // An XSLT script block must never run: either the view is refused or the script is ignored.
            var scripted=View("<p><xsl:value-of select=\"user:Run()\" xmlns:user=\"urn:user\"/></p>").Replace("<xsl:output","<msxsl:script xmlns:msxsl=\"urn:schemas-microsoft-com:xslt\" implements-prefix=\"user\" language=\"C#\">public string Run(){System.IO.File.WriteAllText(\"pwned.txt\",\"x\");return \"RAN\";}</msxsl:script><xsl:output");
            try{var result=EInvoice.ToHtml(Write("Script.xml",Invoice(scripted)));check(!result.Contains("RAN"),"An XSLT script ran");}catch(InvalidDataException){check(true,"An XSLT script block was refused");}
            check(!File.Exists("pwned.txt")&&!File.Exists(Path.Combine(folder,"pwned.txt")),"An XSLT script must not touch the disk");
        }
        finally{Directory.Delete(folder,true);}
    }
}
