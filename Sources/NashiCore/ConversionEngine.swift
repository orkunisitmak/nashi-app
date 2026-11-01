import Foundation

public enum ConversionEngine {
    public static func run(_ request: ConversionRequest, token: CancellationToken = .init(), progress: @escaping ProgressHandler = {_,_ in}) throws -> [URL] {
        guard !request.inputs.isEmpty else { throw NashiError.invalidInput("Choose at least one file.") }
        for url in request.inputs { guard url.isFileURL,FileManager.default.fileExists(atPath:url.path) else { throw NashiError.invalidInput("File not found: \(url.lastPathComponent)") } }
        try token.check()
        let transaction=ExportTransaction()
        defer { transaction.cleanUp() }
        let groups:[[URL]]
        if case .convert("pdf")=request.operation,request.options.combinePDFs == true {
            groups=[request.inputs]
        } else if case .tool(let tool)=request.operation,[ToolKind.createPDF,.mergePDF,.createCollage,.joinVideos,.readQRCode].contains(tool) {
            groups=[request.inputs]
        } else { groups=request.inputs.map{[$0]} }
        for (index,inputs) in groups.enumerated() {
            try token.check()
            let destination=request.options.outputDirectory ?? inputs[0].deletingLastPathComponent()
            let area=try transaction.makeArea(destination:destination)
            var options=request.options;options.outputDirectory=area.directory
            let staged=ConversionRequest(inputs:inputs,operation:request.operation,options:options)
            let outputs=try perform(staged,token:token) { value,label in
                let detail = groups.count>1 ? "File \(index+1) of \(groups.count) · \(label)" : label
                progress(OperationProgress.map(value, from: Double(index)/Double(groups.count)*0.98, to: Double(index+1)/Double(groups.count)*0.98),detail)
            }
            try transaction.record(outputs,in:area)
            progress(Double(index+1)/Double(groups.count)*0.98,"Prepared \(index+1) of \(groups.count) files")
        }
        progress(0.98,"Saving your files…")
        let outputs=try transaction.publish(token:token)
        progress(1,"Done")
        return outputs
    }

    private static func perform(_ request:ConversionRequest,token:CancellationToken,progress:@escaping ProgressHandler) throws -> [URL] {
        var outputs:[URL]=[]
        switch request.operation {
        case .convert(let format):
            if format == "pdf",request.options.combinePDFs == true,request.inputs.allSatisfy({FileCategory.detect($0) == .image}) {
                outputs=try PDFEngine.create(inputs:request.inputs,options:request.options,token:token,progress:progress)
                break
            }
            for input in request.inputs {
                try token.check();progress(OperationProgress.indeterminate,"Processing \(input.lastPathComponent)…")
                switch FileCategory.detect(input) {
                case .image: outputs += try ImageEngine.convert(input,to:format,options:request.options,token:token)
                case .video,.audio: outputs += try MediaEngine.convert(input,to:format,options:request.options,token:token,progress:progress)
                case .pdf where ["png","jpg"].contains(format): outputs += try PDFEngine.render(input,to:format,options:request.options,token:token,progress:progress)
                default: outputs += try DocumentEngine.convert(input,to:format,options:request.options,token:token)
                }
            }
            if format == "pdf",request.options.combinePDFs == true,outputs.count>1 {
                progress(OperationProgress.indeterminate,"Merging PDFs…")
                outputs=try PDFEngine.tool(.mergePDF,inputs:outputs,options:request.options,token:token,progress:progress)
            }
        case .tool(let tool):
            progress(OperationProgress.indeterminate,tool.title)
            switch tool {
            case .readQRCode: outputs=try QRCodeEngine.export(request.inputs,options:request.options,token:token,progress:progress)
            case .createPDF: outputs=try PDFEngine.create(inputs:request.inputs,options:request.options,token:token,progress:progress)
            case .mergePDF,.splitPDF,.organizePDF: outputs=try PDFEngine.tool(tool,inputs:request.inputs,options:request.options,token:token,progress:progress)
            case .extractArchive:
                for input in request.inputs { try token.check();outputs += try DocumentEngine.extractArchive(input,options:request.options,token:token) }
            case .compress,.removeMetadata:
                for input in request.inputs {
                    try token.check()
                    switch FileCategory.detect(input) {
                    case .image: outputs += try ImageEngine.tool(tool,inputs:[input],options:request.options,token:token)
                    case .video where tool == .removeMetadata && input.pathExtension.lowercased()=="gif":
                        outputs += try ImageEngine.tool(tool,inputs:[input],options:request.options,token:token)
                    case .pdf: outputs += try PDFEngine.tool(tool,inputs:[input],options:request.options,token:token,progress:progress)
                    case .audio,.video: outputs += try MediaEngine.tool(tool,inputs:[input],options:request.options,token:token,progress:progress)
                    default: throw NashiError.unsupported("This tool does not support \(input.pathExtension.uppercased()) files.")
                    }
                }
            case .editImage,.annotateImage,.frameImage,.cropImage,.createCollage,.redactImage:
                outputs=try ImageEngine.tool(tool,inputs:request.inputs,options:request.options,token:token)
            default: outputs=try MediaEngine.tool(tool,inputs:request.inputs,options:request.options,token:token,progress:progress)
            }
        }
        try token.check();try OutputFiles.validate(outputs)
        return outputs
    }
}
