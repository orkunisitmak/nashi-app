import Foundation
import PDFKit

/// Page labels (i, ii, A-1…) number pages by position. Metadata cleanup keeps every page in order,
/// so the catalog's number tree stays valid for the cleaned copy, as on Windows (`CopyPageLabels`).
/// PDFKit cannot write the tree, so it is appended to the saved copy as an incremental update.
enum PDFPageLabels {
    static let refusal="This PDF contains custom page labels. Metadata cleanup cannot safely preserve those features. No copy was exported."
    private static func refused()->NashiError { .unsupported(refusal) }

    /// The source's `/PageLabels` as one flat number tree in PDF syntax, or nil when it has none.
    /// Anything that cannot be copied faithfully is refused.
    static func numberTree(of document:PDFDocument) throws -> String? {
        guard let catalog=document.documentRef?.catalog else { return nil }
        var object:CGPDFObjectRef?
        guard CGPDFDictionaryGetObject(catalog,"PageLabels",&object) else { return nil }
        var root:CGPDFDictionaryRef?
        guard CGPDFDictionaryGetDictionary(catalog,"PageLabels",&root),let root else { throw refused() }
        var entries:[String]=[],last = -1,count=0
        func label(_ dictionary:CGPDFDictionaryRef) throws -> String {
            var parts:[String]=[],valid=true
            CGPDFDictionaryApplyBlock(dictionary,{ key,value,_ in
                let key=name(key)
                switch CGPDFObjectGetType(value) {
                case .null: break
                case .boolean: var flag:CGPDFBoolean=0;_=CGPDFObjectGetValue(value,.boolean,&flag);parts.append("\(key) \(flag != 0 ? "true":"false")")
                case .integer: var number:CGPDFInteger=0;_=CGPDFObjectGetValue(value,.integer,&number);parts.append("\(key) \(number)")
                case .name: var text:UnsafePointer<CChar>?;_=CGPDFObjectGetValue(value,.name,&text);if let text { parts.append("\(key) \(self.name(text))") } else { valid=false }
                case .string:
                    var string:CGPDFStringRef?;_=CGPDFObjectGetValue(value,.string,&string)
                    guard let string,let bytes=CGPDFStringGetBytePtr(string) else { valid=false;break }
                    // Hex keeps the prefix's bytes as they are, whatever its text encoding.
                    parts.append("\(key) <\(UnsafeBufferPointer(start:bytes,count:CGPDFStringGetLength(string)).map{String(format:"%02X",$0)}.joined())>")
                default: valid=false
                }
                return valid
            },nil)
            guard valid else { throw refused() }
            // Core Graphics does not promise the keys in file order; sorted, the tree can be compared when it is read back.
            return "<< \(parts.sorted().joined(separator:" ")) >>"
        }
        func visit(_ node:CGPDFDictionaryRef,depth:Int) throws {
            count+=1;guard depth<=32,count<=100_000 else { throw refused() }
            var numbers:CGPDFArrayRef?,kids:CGPDFArrayRef?
            if CGPDFDictionaryGetArray(node,"Nums",&numbers),let numbers {
                let length=CGPDFArrayGetCount(numbers);guard length%2==0 else { throw refused() }
                for index in stride(from:0,to:length,by:2) {
                    var page:CGPDFInteger=0,dictionary:CGPDFDictionaryRef?
                    count+=1
                    // One flat tree is only the same numbering when the keys already ascend.
                    guard count<=100_000,CGPDFArrayGetInteger(numbers,index,&page),page>last,CGPDFArrayGetDictionary(numbers,index+1,&dictionary),let dictionary else { throw refused() }
                    last=page;entries.append("\(page) \(try label(dictionary))")
                }
            }
            if CGPDFDictionaryGetArray(node,"Kids",&kids),let kids {
                for index in 0..<CGPDFArrayGetCount(kids) {
                    var kid:CGPDFDictionaryRef?
                    guard CGPDFArrayGetDictionary(kids,index,&kid),let kid else { throw refused() }
                    try visit(kid,depth:depth+1)
                }
            }
        }
        try visit(root,depth:0)
        return "<< /Nums [\(entries.joined(separator:" "))] >>"
    }

    private static func name(_ text:UnsafePointer<CChar>)->String {
        "/"+String(cString:text).utf8.map{ byte in
            (33...126).contains(byte) && !"()<>[]{}/%#".utf8.contains(byte) ? String(UnicodeScalar(byte)):String(format:"#%02X",byte)
        }.joined()
    }

    /// `pdf` with `tree` as its catalog's `/PageLabels`. `pdf` is a copy PDFKit has just written:
    /// one classic cross-reference table and a plain catalog. Any other shape is refused.
    static func adding(_ tree:String,to pdf:Data) throws -> Data {
        // Latin-1 keeps every byte of the trailer and the catalog as it is.
        func text(_ range:Range<Int>)->String { String(data:pdf.subdata(in:range),encoding:.isoLatin1) ?? "" }
        func first(_ pattern:String,in source:String)->[Int]? {
            guard let match=try? NSRegularExpression(pattern:pattern).firstMatch(in:source,range:NSRange(source.startIndex...,in:source)) else { return nil }
            let numbers=(1..<match.numberOfRanges).compactMap{Range(match.range(at:$0),in:source).flatMap{Int(source[$0])}}
            return numbers.count==match.numberOfRanges-1 ? numbers:nil
        }
        guard let start=pdf.range(of:Data("startxref".utf8),options:.backwards),
              let table=first(#"^\s*(\d+)\s"#,in:text(start.upperBound..<min(pdf.count,start.upperBound+32)))?.first,
              table+4<start.lowerBound,text(table..<table+4)=="xref",
              let keyword=pdf.range(of:Data("trailer".utf8),in:table..<start.lowerBound) else { throw refused() }
        let trailer=text(keyword.upperBound..<start.lowerBound).trimmingCharacters(in:.whitespacesAndNewlines)
        guard trailer.hasPrefix("<<"),trailer.hasSuffix(">>"),!trailer.contains("/Prev"),!trailer.contains("/XRefStm"),
              let root=first(#"/Root\s+(\d+)\s+(\d+)\s+R"#,in:trailer) else { throw refused() }
        // Subsection headers have two numbers and entries three tokens, so the catalog's entry is found by position.
        let tokens=text(table+4..<keyword.lowerBound).split(whereSeparator:\.isWhitespace)
        var index=0,offset:Int?
        while index+1<tokens.count,offset==nil {
            guard let from=Int(tokens[index]),let size=Int(tokens[index+1]),size>=0,index+2+size*3<=tokens.count else { throw refused() }
            if (from..<from+size).contains(root[0]) {
                let entry=index+2+(root[0]-from)*3
                guard tokens[entry+2]=="n",Int(tokens[entry+1])==root[1] else { throw refused() }
                offset=Int(tokens[entry])
            }
            index+=2+size*3
        }
        guard let offset,offset>=0,offset<table else { throw refused() }
        let bytes=[UInt8](pdf[pdf.startIndex+offset..<pdf.startIndex+min(table,offset+65_536)])
        let header=Array("\(root[0]) \(root[1]) obj".utf8)
        var cursor=header.count
        func skipSpace() { while cursor<bytes.count,[0,9,10,12,13,32].contains(bytes[cursor]) { cursor+=1 } }
        func at(_ word:String)->Bool { bytes[cursor...].starts(with:Array(word.utf8)) }
        guard bytes.starts(with:header) else { throw refused() }
        skipSpace()
        let open=cursor
        guard at("<<") else { throw refused() }
        var depth=0,close:Int?
        while cursor<bytes.count,close==nil {
            switch bytes[cursor] {
            case UInt8(ascii:"<") where cursor+1<bytes.count && bytes[cursor+1]==UInt8(ascii:"<"): depth+=1;cursor+=2
            case UInt8(ascii:">") where cursor+1<bytes.count && bytes[cursor+1]==UInt8(ascii:">"): depth-=1;if depth==0 { close=cursor };cursor+=2
            case UInt8(ascii:"<"): while cursor<bytes.count,bytes[cursor] != UInt8(ascii:">") { cursor+=1 };cursor+=1
            case UInt8(ascii:"("):
                var nesting=0
                repeat {
                    if bytes[cursor]==UInt8(ascii:"\\") { cursor+=1 } else if bytes[cursor]==UInt8(ascii:"(") { nesting+=1 } else if bytes[cursor]==UInt8(ascii:")") { nesting-=1 }
                    cursor+=1
                } while nesting>0 && cursor<bytes.count
            case UInt8(ascii:"%"): throw refused()
            default: cursor+=1
            }
        }
        guard let close else { throw refused() }
        skipSpace()
        let dictionary=String(data:Data(bytes[open..<close]),encoding:.isoLatin1) ?? ""
        guard at("endobj"),!dictionary.contains("/PageLabels") else { throw refused() }
        var result=pdf
        if result.last != 10 { result.append(10) }
        let object=result.count
        guard let catalog="\(root[0]) \(root[1]) obj\n\(dictionary) /PageLabels \(tree) >>\nendobj\n".data(using:.isoLatin1) else { throw refused() }
        result.append(catalog)
        let update=result.count
        guard let section="xref\n\(root[0]) 1\n\(String(format:"%010d %05d n \n",object,root[1]))trailer\n\(trailer.dropLast(2)) /Prev \(table) >>\nstartxref\n\(update)\n%%EOF\n".data(using:.isoLatin1) else { throw refused() }
        result.append(section)
        return result
    }

    /// The cleaned copy's bytes with the source's labels, read back before anything is saved.
    /// PDFKit's own `label` is no check: it misreads trees split over `/Kids`.
    static func preserving(_ tree:String,from source:PDFDocument,in cleaned:PDFDocument) throws -> Data {
        guard let written=cleaned.dataRepresentation() else { throw NashiError.failed("Could not save PDF.") }
        let bytes=try adding(tree,to:written)
        guard let saved=PDFDocument(data:bytes),saved.pageCount==source.pageCount,try numberTree(of:saved)==tree else { throw refused() }
        return bytes
    }
}
