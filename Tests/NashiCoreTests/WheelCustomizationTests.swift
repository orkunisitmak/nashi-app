import XCTest
@testable import NashiCore

final class WheelCustomizationTests:XCTestCase {
    func testVisibleRemovesHiddenAndKeepsOrder() {
        XCTAssertEqual(WheelCustomization.visible(["a","b","c","d"],hidden:["b","d"],id:{$0}),["a","c"])
    }
    func testUnknownHiddenIdsAreIgnored() {
        XCTAssertEqual(WheelCustomization.visible(["a","b"],hidden:["zzz"],id:{$0}),["a","b"])
    }
    func testEmptyHiddenShowsEverything() {
        XCTAssertEqual(WheelCustomization.visible(["a"],hidden:[],id:{$0}),["a"])
    }
    func testAllHiddenEmptiesUnlessKeepOne() {
        XCTAssertEqual(WheelCustomization.visible(["a","b"],hidden:["a","b"],id:{$0}),[])
        XCTAssertEqual(WheelCustomization.visible(["a","b"],hidden:["a","b"],id:{$0},keepOne:true),["a","b"])
    }
    func testConvertSamplesShowEveryTarget() {
        XCTAssertEqual(Set(WheelCustomization.convertSamples.flatMap{FeatureCatalog.formats(for:$0.inputs)}),Set(FeatureCatalog.conversionMatrix.values.joined()))
    }
    func testToolSamplesShowEveryTool() {
        XCTAssertEqual(Set(WheelCustomization.toolSamples.flatMap{FeatureCatalog.tools(for:$0.inputs)}),Set(ToolKind.allCases))
    }
    func testGroupsListEachItemOnce() {
        let formats=WheelCustomization.formatGroups.flatMap(\.1)
        XCTAssertEqual(formats.count,Set(formats).count)
        XCTAssertEqual(Set(formats),Set(WheelCustomization.convertTargets))
        let tools=WheelCustomization.toolGroups.flatMap(\.1)
        XCTAssertEqual(tools.count,Set(tools).count)
        XCTAssertEqual(tools.count,27)
        XCTAssertEqual(Set(tools),Set(ToolKind.allCases))
    }
    func testFirstSampleShowing() {
        XCTAssertEqual(WheelCustomization.firstConvertSample(showing:"jpg")?.id,"png")
        XCTAssertEqual(WheelCustomization.firstConvertSample(showing:"html")?.id,"docx")
        XCTAssertEqual(WheelCustomization.firstToolSample(showing:.mergePDF)?.id,"pdf+pdf")
        XCTAssertEqual(WheelCustomization.firstToolSample(showing:.extractArchive)?.id,"zip")
        XCTAssertNil(WheelCustomization.firstConvertSample(showing:"nope"))
    }
    func testSampleTitles() {
        XCTAssertEqual(WheelCustomization.toolSamples.first{$0.id=="png+png"}?.title,"2 × PNG")
        XCTAssertEqual(WheelCustomization.convertSamples.first?.title,"PNG")
    }
}
