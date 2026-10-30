import Foundation

/// Namespace-aware editing for existing custom XMP fields. XML entities never
/// load external resources; arbitrary new schemas require a declared namespace.
enum XMPPacketEditor {
    private static let rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#"
    private static let schemas=["dc":"http://purl.org/dc/elements/1.1/","xmp":"http://ns.adobe.com/xap/1.0/","photoshop":"http://ns.adobe.com/photoshop/1.0/","tiff":"http://ns.adobe.com/tiff/1.0/","exif":"http://ns.adobe.com/exif/1.0/","xmpRights":"http://ns.adobe.com/xap/1.0/rights/"]
    static func isXMP(_ key:String)->Bool { key.hasPrefix("{XMP-") || key.hasPrefix("XMP-") }
    private static func split(_ key:String) throws -> (String,String) {
        let value=key.replacingOccurrences(of:"{XMP-",with:"").replacingOccurrences(of:"XMP-",with:"").replacingOccurrences(of:"}.",with:":")
        let pieces=value.split(separator:":",maxSplits:1).map(String.init)
        guard pieces.count==2,pieces.allSatisfy({$0.range(of:"^[A-Za-z_][A-Za-z0-9_.-]*$",options:.regularExpression) != nil}) else { throw NashiError.invalidInput("Invalid XMP field: \(key)") }
        return (pieces[0],pieces[1])
    }
    private static func document(_ data:Data) throws -> XMLDocument {
        let base=Data("<x:xmpmeta xmlns:x='adobe:ns:meta/'><rdf:RDF xmlns:rdf='\(rdf)'><rdf:Description rdf:about=''/></rdf:RDF></x:xmpmeta>".utf8)
        // The XML parser also accepts UTF-16/32 packets; dropping NUL bytes exposes
        // their ASCII declarations to the same check as UTF-8 text.
        let text=String(decoding:data.filter { $0 != 0 },as:UTF8.self)
        guard text.range(of:"<!DOCTYPE",options:.caseInsensitive)==nil,text.range(of:"<!ENTITY",options:.caseInsensitive)==nil else { throw NashiError.invalidInput("XMP declarations with external entities or DTDs are not supported.") }
        return try XMLDocument(data:data.isEmpty ? base:data,options:[.nodePreserveAll,.nodeLoadExternalEntitiesNever])
    }
    private static func descriptions(_ doc:XMLDocument) throws -> [XMLElement] {
        // Apple's XPath namespace-uri() predicate does not reliably match parsed
        // namespace declarations. Match the local name, then inspect native URIs.
        try doc.nodes(forXPath:"//*[local-name()='Description']").compactMap{$0 as? XMLElement}.filter{$0.uri==rdf}
    }
    static func fields(_ data:Data) throws -> [String:String] {
        guard !data.isEmpty else { return [:] }
        let doc=try document(data);var result:[String:String]=[:]
        for description in try descriptions(doc) {
            for attribute in description.attributes ?? [] where attribute.prefix != "rdf" && !(attribute.prefix ?? "").isEmpty {
                guard let prefix=attribute.prefix,let name=attribute.localName else { continue }
                result["{XMP-\(prefix)}.\(name)"]=attribute.stringValue ?? ""
            }
            for child in description.children?.compactMap({$0 as? XMLElement}) ?? [] {
                guard let prefix=child.prefix,!prefix.isEmpty,prefix != "rdf",let name=child.localName else { continue }
                let list=try child.nodes(forXPath:".//*[local-name()='li']").filter{$0.uri==rdf}
                if list.count>1,let values=try? JSONSerialization.data(withJSONObject:list.map{$0.stringValue ?? ""}) {
                    result["{XMP-\(prefix)}.\(name)"]=String(decoding:values,as:UTF8.self)
                } else { result["{XMP-\(prefix)}.\(name)"]=list.first?.stringValue ?? child.stringValue ?? "" }
            }
        }
        return result
    }
    static func edit(_ data:Data,updates:[String:String],removals:[String]) throws -> Data {
        let doc=try document(data)
        guard let first=try descriptions(doc).first else { throw NashiError.invalidInput("This XMP packet has no editable description.") }
        for key in removals {
            let (prefix,name)=try split(key)
            for description in try descriptions(doc) {
                for attribute in description.attributes ?? [] where attribute.prefix==prefix && attribute.localName?.caseInsensitiveCompare(name) == .orderedSame { attribute.detach() }
                for child in description.children ?? [] where child.prefix==prefix && child.localName?.caseInsensitiveCompare(name) == .orderedSame { child.detach() }
            }
        }
        for (key,value) in updates.sorted(by:{$0.key<$1.key}) {
            let (prefix,name)=try split(key)
            var existing:XMLElement?,owner=first
            for description in try descriptions(doc) {
                if let attribute=description.attributes?.first(where:{$0.prefix==prefix && $0.localName?.caseInsensitiveCompare(name) == .orderedSame}) { attribute.stringValue=value;existing=nil;owner=description;break }
                if let child=description.children?.compactMap({$0 as? XMLElement}).first(where:{$0.prefix==prefix && $0.localName?.caseInsensitiveCompare(name) == .orderedSame}) { existing=child;owner=description;break }
            }
            if owner.attributes?.contains(where:{$0.prefix==prefix && $0.localName?.caseInsensitiveCompare(name) == .orderedSame}) == true { continue }
            let node:XMLElement
            if let existing { node=existing }
            else {
                let uri=owner.resolveNamespace(forName:prefix+":"+name)?.stringValue ?? schemas[prefix]
                guard let uri,!uri.isEmpty else { throw NashiError.invalidInput("The custom XMP namespace \(prefix) is not declared in this file.") }
                owner.addNamespace(XMLNode.namespace(withName:prefix,stringValue:uri) as! XMLNode)
                let local=prefix=="dc" ? name.prefix(1).lowercased()+name.dropFirst():name
                node=XMLElement(name:prefix+":"+local,uri:uri);owner.addChild(node)
            }
            let oldContainer=node.children?.compactMap({$0 as? XMLElement}).first(where:{$0.uri==rdf && ["Alt","Seq","Bag"].contains($0.localName ?? "")})
            let values=(try? JSONSerialization.jsonObject(with:Data(value.utf8))) as? [String]
            if let oldContainer {
                oldContainer.setChildren(nil)
                for text in values ?? [value] {
                    let li=XMLElement(name:"rdf:li",uri:rdf);li.stringValue=text
                    if oldContainer.localName=="Alt" { li.addAttribute(XMLNode.attribute(withName:"xml:lang",stringValue:"x-default") as! XMLNode) }
                    oldContainer.addChild(li)
                }
            } else if prefix=="dc",["title","description","rights","creator","subject"].contains(name.lowercased()) {
                let kind=["title","description","rights"].contains(name.lowercased()) ? "Alt":name.lowercased()=="creator" ? "Seq":"Bag"
                let container=XMLElement(name:"rdf:"+kind,uri:rdf);node.setChildren([container])
                for text in values ?? [value] { let li=XMLElement(name:"rdf:li",uri:rdf);li.stringValue=text;if kind=="Alt" { li.addAttribute(XMLNode.attribute(withName:"xml:lang",stringValue:"x-default") as! XMLNode) };container.addChild(li) }
            } else { node.stringValue=value }
        }
        // ExifTool accepts packets beginning at xpacket or the xmpmeta root.
        // Remove Foundation's XML declaration from this standalone UTF-8 packet.
        let serialized=String(decoding:doc.xmlData(options:[.nodeCompactEmptyElement]),as:UTF8.self)
        let packet=serialized.replacingOccurrences(of:"^<\\?xml[^?]*\\?>",with:"",options:.regularExpression)
        return Data(packet.trimmingCharacters(in:.whitespacesAndNewlines).utf8)
    }
}
