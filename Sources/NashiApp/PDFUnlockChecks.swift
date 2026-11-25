import AppKit
import PDFKit
import NashiCore

@MainActor enum PDFUnlockChecks {
    static func run()throws->[String] {
        let root=try OutputFiles.temporaryDirectory();defer { try? FileManager.default.removeItem(at:root) }
        let url=root.appendingPathComponent("Protected.pdf"),document=PDFDocument()
        let page=PDFPage();page.setBounds(CGRect(x:0,y:0,width:200,height:300),for:.mediaBox);document.insert(page,at:0)
        guard document.write(to:url,withOptions:[.ownerPasswordOption:"fixture-owner",.userPasswordOption:"fixture-password"]) else { throw UIAuditError("Could not write PDF unlock fixture") }
        let original=try Data(contentsOf:url),model=PDFUnlockModel()
        for password in ["","wrong"] {
            model.password=password
            do { _=try model.open(url);throw UIAuditError("Locked PDF opened without the correct password") }
            catch is NashiError { guard model.needsPassword else { throw UIAuditError("PDF retry controls were not enabled") } }
        }
        model.password="fixture-password"
        guard try model.open(url).pageCount==1,!model.needsPassword else { throw UIAuditError("Correct PDF password did not open the editor document") }
        model.reset()
        guard model.password.isEmpty,!model.needsPassword,try Data(contentsOf:url)==original else { throw UIAuditError("PDF password reset changed the original or retained session state") }
        return ["Locked PDF requests a password","Wrong PDF password allows retry","Correct password opens PDF","PDF session reset clears password and preserves original"]
    }
}
