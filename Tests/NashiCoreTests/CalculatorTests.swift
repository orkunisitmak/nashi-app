import XCTest
@testable import NashiCore

final class CalculatorTests:XCTestCase {
    private let locale=Locale(identifier:"en_US")
    private let rates=CurrencyRates(base:"USD",rates:["USD":1,"AED":3.6725,"EUR":0.92,"TRY":34.25,"GBP":0.79],updated:Date(timeIntervalSince1970:1_793_318_551))

    private func answer(_ query:String,local:String="TRY",file:StaticString=#filePath,line:UInt=#line)->Calculator.Answer? {
        guard case .answer(let answer)? = Calculator.evaluate(query,rates:rates,localCurrency:local,locale:locale) else { return nil }
        return answer
    }

    func testArithmetic() {
        XCTAssertEqual(answer("10+10")?.text,"20")
        XCTAssertEqual(answer("10+10")?.kind,.math)
        XCTAssertEqual(answer("2 + 3 * 4")?.value,14)
        XCTAssertEqual(answer("(2+3)*4")?.value,20)
        XCTAssertEqual(answer("2^10")?.value,1024)
        XCTAssertEqual(answer("2**3")?.value,8)
        XCTAssertEqual(answer("-2^2")?.value,-4)
        XCTAssertEqual(answer("10/4")?.text,"2.5")
        XCTAssertEqual(answer("0.1+0.2")?.text,"0.3")
        XCTAssertEqual(answer("7 × 6")?.value,42)
        XCTAssertEqual(answer("9 ÷ 3")?.value,3)
        XCTAssertEqual(answer("3x4")?.value,12)
        XCTAssertEqual(answer("10 mod 3")?.value,1)
        XCTAssertEqual(answer("5!")?.value,120)
        XCTAssertEqual(answer("1,000 * 3")?.text,"3,000")
        XCTAssertEqual(answer("1,5 * 2")?.value,3)
        XCTAssertEqual(answer("2(3+4)")?.value,14)
    }

    func testFunctionsConstantsAndPercentages() {
        XCTAssertEqual(answer("sqrt(16)")?.value,4)
        XCTAssertEqual(answer("√9")?.value,3)
        XCTAssertEqual(answer("log(1000)")?.value,3)
        XCTAssertEqual(answer("round(2.6)")?.value,3)
        XCTAssertEqual(answer("2pi")?.text,"6.2831853072")
        XCTAssertEqual(answer("50 + 10%")?.value,55)
        XCTAssertEqual(answer("80 - 25%")?.value,60)
        XCTAssertEqual(answer("20% of 50")?.value,10)
        XCTAssertEqual(answer("15%")?.value,0.15)
    }

    func testPlainTextAndInvalidInputShowNothing() {
        for query in ["","  ","42","hello","10/0","2+","(1+2","USD","Nashi","report.pdf","sqrt(-1)","171!","√-4"] {
            XCTAssertNil(Calculator.evaluate(query,rates:rates,localCurrency:"TRY",locale:locale),query)
        }
    }

    func testCurrencyConvertsToLocalCurrencyAndExplicitTargets() throws {
        let local=try XCTUnwrap(answer("1 USD"))
        XCTAssertEqual(local.kind,.currency)
        XCTAssertEqual(local.text,"34.25 TRY")
        XCTAssertEqual(local.copyText,"34.25")
        XCTAssertEqual(local.detail,"1.00 USD =")
        XCTAssertEqual(local.ratesUpdated,rates.updated)
        XCTAssertEqual(answer("1 USD to AED")?.text,"3.67 AED")
        XCTAssertEqual(try XCTUnwrap(answer("1 usd to aed")?.value),3.6725,accuracy:1e-9)
        XCTAssertEqual(answer("usd to aed")?.text,"3.67 AED")
        XCTAssertEqual(answer("100 aed in usd")?.text,"27.23 USD")
        XCTAssertEqual(answer("$20")?.text,"685.00 TRY")
        XCTAssertEqual(answer("€10 to gbp")?.text,"8.59 GBP")
        XCTAssertEqual(answer("10 dollars to euros")?.text,"9.20 EUR")
        XCTAssertEqual(answer("2*5 USD → EUR")?.text,"9.20 EUR")
        XCTAssertEqual(answer("1,000 TRY to USD")?.text,"29.20 USD")
        // The local currency itself shows its US dollar value.
        XCTAssertEqual(answer("100 TRY")?.text,"2.92 USD")
        XCTAssertEqual(answer("1 USD",local:"USD")?.text,"0.92 EUR")
        XCTAssertNil(answer("1 USD to XYZ"))
    }

    func testCurrencyWithoutRatesAsksForThem() {
        XCTAssertEqual(Calculator.evaluate("1 USD to AED",rates:nil,localCurrency:"TRY",locale:locale),.needsRates(from:"USD",to:"AED"))
        XCTAssertEqual(Calculator.evaluate("5 eur",rates:nil,localCurrency:"TRY",locale:locale),.needsRates(from:"EUR",to:"TRY"))
    }

    func testUnitConversions() {
        XCTAssertEqual(answer("5 km to mi")?.text,"3.106856 mi")
        XCTAssertEqual(answer("5 km to mi")?.kind,.unit)
        XCTAssertEqual(answer("5km in miles")?.text,"3.106856 mi")
        XCTAssertEqual(answer("6 ft to cm")?.text,"182.88 cm")
        XCTAssertEqual(answer("1 inch to mm")?.text,"25.4 mm")
        XCTAssertEqual(answer("100 c to f")?.text,"212 °F")
        XCTAssertEqual(answer("1 second to years")?.text,"0.0000000317 yr","Tiny results keep their digits instead of rounding to zero")
        XCTAssertEqual(answer("1 second to years")?.copyText,"0.0000000317")
        XCTAssertEqual(answer("100 KB to TB")?.text,"0.0000001 TB")
        XCTAssertEqual(answer("1.5 KB to GB")?.text,"0.0000015 GB")
        XCTAssertEqual(answer("32 °F to °C")?.text,"0 °C")
        XCTAssertEqual(answer("0 kelvin to celsius")?.text,"-273.15 °C")
        XCTAssertEqual(answer("1 lb to kg")?.text,"0.453592 kg")
        XCTAssertEqual(answer("2 pounds in grams")?.text,"907.18474 g")
        XCTAssertEqual(answer("1 gallon to liters")?.text,"3.785412 l")
        XCTAssertEqual(answer("3 cups to ml")?.text,"709.76471 ml")
        XCTAssertEqual(answer("1 GB to MB")?.text,"1,000 MB")
        XCTAssertEqual(answer("1 GiB to MiB")?.text,"1,024 MiB")
        XCTAssertEqual(answer("8 bits to bytes")?.text,"1 B")
        XCTAssertEqual(answer("90 minutes to hours")?.text,"1.5 h")
        XCTAssertEqual(answer("1 week in days")?.text,"7 d")
        XCTAssertEqual(answer("100 km/h to mph")?.text,"62.137119 mph")
        XCTAssertEqual(answer("1 acre to m2")?.text,"4,046.856422 m²")
        XCTAssertEqual(answer("1000 square feet to square meters")?.text,"92.90304 m²")
        XCTAssertEqual(answer("180 degrees to radians")?.text,"3.141593 rad")
        XCTAssertEqual(answer("(2+3) km to m")?.text,"5,000 m")
        XCTAssertNil(answer("5 km to kg"))
        XCTAssertNil(answer("5 km"))
    }

    func testLocalizedNumbersAndCopyText() throws {
        guard case .answer(let answer)? = Calculator.evaluate("1234.5*2",locale:Locale(identifier:"tr_TR")) else { return XCTFail("No answer") }
        XCTAssertEqual(answer.text,"2.469")
        XCTAssertEqual(answer.copyText,"2469")
        guard case .answer(let money)? = Calculator.evaluate("1000 usd",rates:rates,localCurrency:"TRY",locale:Locale(identifier:"tr_TR")) else { return XCTFail("No answer") }
        XCTAssertEqual(money.text,"34.250,00 TRY")
        XCTAssertEqual(money.copyText,"34250,00")
        XCTAssertEqual(self.answer("2^60")?.text,"1.1529215E18")
    }

    func testCalculatorCopyIsTranslatedIncludingDatesWithSpaces() {
        XCTAssertEqual(AppLocalization.text("Exchange rates: 30 Eki 2026 12:56",language:"tr"),"Döviz kurları: 30 Eki 2026 12:56")
        XCTAssertEqual(AppLocalization.text("Exchange rates: 30 oct. 2026 à 13:00",language:"fr"),"Taux de change\u{00A0}: 30 oct. 2026 à 13:00")
        XCTAssertEqual(AppLocalization.text("Getting today’s exchange rates for USD → TRY…",language:"tr"),"USD → TRY için güncel döviz kurları alınıyor…")
        for source in ["Calculate or convert","Calculator","Local currency","Copied","Try again","Return copies the answer"] {
            XCTAssertNotEqual(AppLocalization.text(source,language:"tr"),source,source)
            XCTAssertNotEqual(AppLocalization.text(source,language:"fr"),source,source)
        }
    }

    func testNumbersFollowTheLocaleSeparators() {
        let turkish=Locale(identifier:"tr_TR")
        func value(_ query:String,_ locale:Locale)->Double? {
            guard case .answer(let answer)? = Calculator.evaluate(query,rates:rates,localCurrency:"TRY",locale:locale) else { return nil }
            return answer.value
        }
        // Turkish writes one thousand as 1.000 and one and a half as 1,5.
        XCTAssertEqual(value("1.000 usd",turkish),34_250)
        XCTAssertEqual(value("1,5 * 2",turkish),3)
        XCTAssertEqual(value("1.234,5 + 0,5",turkish),1235)
        XCTAssertEqual(value("2.469 * 1",turkish),2469,"A displayed Turkish answer can be typed back in")
        XCTAssertEqual(value("1.5 * 2",turkish),3,"A dot that cannot group thousands is still a decimal point")
        XCTAssertEqual(value("1.000.000 / 1.000",turkish),1000)
        XCTAssertEqual(value("0.125 * 8",turkish),1,"A leading zero is never a thousands group")
        XCTAssertEqual(value("0.250 usd",turkish),8.5625)
        XCTAssertEqual(value("0.125 kg to g",turkish),125)
        XCTAssertNil(value("0.125.000 + 1",turkish))
        XCTAssertEqual(value("0,125 * 8",locale),1)
        XCTAssertEqual(value("1,000.5 + 1",locale),1001.5)
        XCTAssertEqual(value("1,5 * 2",locale),3,"A comma that cannot group thousands is still a decimal point")
        XCTAssertEqual(value(".5 * 4",locale),2)
        XCTAssertNil(value("1.234,5 + 1",locale))
        XCTAssertNil(value("1,23,4 + 1",locale))
    }

    func testCurrencyMissingFromDownloadedRatesShowsNothing() {
        // Fetching again would not add the code, so this is not a pending answer.
        XCTAssertNil(Calculator.evaluate("1 bitcoin to usd",rates:rates,localCurrency:"TRY",locale:locale))
        XCTAssertNil(Calculator.evaluate("10 CHF",rates:rates,localCurrency:"TRY",locale:locale))
        XCTAssertEqual(Calculator.evaluate("10 CHF",rates:nil,localCurrency:"TRY",locale:locale),.needsRates(from:"CHF",to:"TRY"))
    }

    func testLocalCurrencyPrefersEnvironmentThenSettingThenRegion() throws {
        let suite="Nashi.CalculatorTests."+UUID().uuidString,defaults=try XCTUnwrap(UserDefaults(suiteName:suite))
        defer { defaults.removePersistentDomain(forName:suite) }
        let region=Locale(identifier:"tr_TR")
        XCTAssertEqual(Calculator.localCurrency(environment:[:],defaults:defaults,locale:region),"TRY")
        defaults.set("auto",forKey:Calculator.currencyPreferenceKey)
        XCTAssertEqual(Calculator.localCurrency(environment:[:],defaults:defaults,locale:region),"TRY")
        defaults.set("EUR",forKey:Calculator.currencyPreferenceKey)
        XCTAssertEqual(Calculator.localCurrency(environment:[:],defaults:defaults,locale:region),"EUR")
        XCTAssertEqual(Calculator.localCurrency(environment:["NASHI_CURRENCY":"gbp"],defaults:defaults,locale:region),"GBP")
        XCTAssertEqual(Calculator.localCurrency(environment:["NASHI_CURRENCY":"pounds"],defaults:defaults,locale:region),"EUR")
    }

    // MARK: Command line

    private func run(_ body:()->Int32)->(code:Int32,output:String,error:String) {
        let directory=FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString,isDirectory:true)
        try? FileManager.default.createDirectory(at:directory,withIntermediateDirectories:true)
        defer { try? FileManager.default.removeItem(at:directory) }
        let outURL=directory.appendingPathComponent("stdout"),errURL=directory.appendingPathComponent("stderr")
        FileManager.default.createFile(atPath:outURL.path,contents:nil);FileManager.default.createFile(atPath:errURL.path,contents:nil)
        guard let out=try? FileHandle(forWritingTo:outURL),let err=try? FileHandle(forWritingTo:errURL) else { return (-1,"","") }
        fflush(stdout);fflush(stderr)
        let savedOut=dup(STDOUT_FILENO),savedErr=dup(STDERR_FILENO)
        dup2(out.fileDescriptor,STDOUT_FILENO);dup2(err.fileDescriptor,STDERR_FILENO)
        let code=body()
        fflush(stdout);fflush(stderr)
        dup2(savedOut,STDOUT_FILENO);dup2(savedErr,STDERR_FILENO);close(savedOut);close(savedErr)
        try? out.close();try? err.close()
        return (code,(try? String(contentsOf:outURL,encoding:.utf8)) ?? "",(try? String(contentsOf:errURL,encoding:.utf8)) ?? "")
    }
    private func temporaryCache()->URL { FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString,isDirectory:true).appendingPathComponent("rates.json") }

    func testCalcCommandRoutesMathAndUnitsThroughTheEntryPoint() {
        let math=run { NashiCommandLine.run(arguments:["nashi-cli","calc","10+10"]) }
        XCTAssertEqual(math.code,0);XCTAssertEqual(math.output,"10+10 = 20\n");XCTAssertEqual(math.error,"")
        let spaced=run { NashiCommandLine.run(arguments:["nashi-cli","calc","2","m","to","cm"]) }
        XCTAssertEqual(spaced.code,0);XCTAssertEqual(spaced.output,"2 m = 200 cm\n")
        let nothing=run { NashiCommandLine.run(arguments:["nashi-cli","calc","hello"]) }
        XCTAssertEqual(nothing.code,1);XCTAssertEqual(nothing.output,"");XCTAssertEqual(nothing.error,"Nothing to calculate.\n")
        let usage=run { NashiCommandLine.run(arguments:["nashi-cli","calc"]) }
        XCTAssertEqual(usage.code,64);XCTAssertTrue(usage.error.contains("nashi-cli calc EXPRESSION"))
    }

    func testCalcCommandUsesFreshCachedRatesWithoutDownloading() throws {
        let cache=temporaryCache();defer { try? FileManager.default.removeItem(at:cache.deletingLastPathComponent()) }
        CurrencyRateService.store(CurrencyRates(base:"USD",rates:["TRY":34.25,"EUR":0.92],updated:Date()),at:cache)
        let result=run { NashiCommandLine.calculate("1 USD",environment:["NASHI_CURRENCY":"TRY"],locale:locale,cacheURL:cache,fetch:{ throw NashiError.invalidInput("No download expected.") }) }
        XCTAssertEqual(result.code,0);XCTAssertEqual(result.output,"1.00 USD = 34.25 TRY\n");XCTAssertEqual(result.error,"")
    }

    func testCalcCommandUsesTheSavedCurrencySetting() throws {
        let suite="Nashi.CalculatorTests."+UUID().uuidString,defaults=try XCTUnwrap(UserDefaults(suiteName:suite))
        defer { defaults.removePersistentDomain(forName:suite) }
        defaults.set("EUR",forKey:Calculator.currencyPreferenceKey)
        let cache=temporaryCache();defer { try? FileManager.default.removeItem(at:cache.deletingLastPathComponent()) }
        CurrencyRateService.store(CurrencyRates(base:"USD",rates:["TRY":34.25,"EUR":0.92],updated:Date()),at:cache)
        let result=run { NashiCommandLine.calculate("10 USD",environment:[:],defaults:defaults,locale:locale,cacheURL:cache,fetch:{ throw NashiError.invalidInput("No download expected.") }) }
        XCTAssertEqual(result.output,"10.00 USD = 9.20 EUR\n")
    }

    func testCalcCommandReadsTheCurrencySettingFromTheApp() throws {
        let suite="Nashi.CalculatorTests."+UUID().uuidString,app=try XCTUnwrap(UserDefaults(suiteName:suite))
        defer { app.removePersistentDomain(forName:suite) }
        app.set("EUR",forKey:Calculator.currencyPreferenceKey)
        let standalone=Calculator.appDefaults(current:nil,app:suite)
        XCTAssertEqual(Calculator.localCurrency(environment:[:],defaults:standalone,locale:locale),"EUR")
        XCTAssertTrue(Calculator.appDefaults(current:suite,app:suite) === UserDefaults.standard,"The app reads its own preferences directly")
    }

    func testCalcCommandDownloadsMissingOrStaleRatesAndCachesThem() throws {
        let cache=temporaryCache();defer { try? FileManager.default.removeItem(at:cache.deletingLastPathComponent()) }
        let stale=CurrencyRates(base:"USD",rates:["TRY":30],updated:Date(timeIntervalSinceNow:-5*86400))
        CurrencyRateService.store(stale,at:cache)
        let fresh=CurrencyRates(base:"USD",rates:["TRY":34.25],updated:Date())
        let result=run { NashiCommandLine.calculate("2 usd to try",environment:[:],locale:locale,cacheURL:cache,fetch:{ fresh }) }
        XCTAssertEqual(result.code,0);XCTAssertEqual(result.output,"2.00 USD = 68.50 TRY\n")
        XCTAssertEqual(CurrencyRateService.cached(at:cache),fresh)
    }

    func testCalcCommandReportsOfflineRatesAndKeepsStaleAnswers() throws {
        let cache=temporaryCache();defer { try? FileManager.default.removeItem(at:cache.deletingLastPathComponent()) }
        let offline:@Sendable () async throws->CurrencyRates={ throw URLError(.notConnectedToInternet) }
        let missing=run { NashiCommandLine.calculate("1 USD to TRY",environment:[:],locale:locale,cacheURL:cache,fetch:offline) }
        XCTAssertEqual(missing.code,1);XCTAssertEqual(missing.output,"");XCTAssertEqual(missing.error,"Currency rates are unavailable right now.\n")
        XCTAssertNil(CurrencyRateService.cached(at:cache))
        // Old rates still answer when the download fails.
        let stale=CurrencyRates(base:"USD",rates:["TRY":30],updated:Date(timeIntervalSinceNow:-5*86400))
        CurrencyRateService.store(stale,at:cache)
        let kept=run { NashiCommandLine.calculate("1 USD to TRY",environment:[:],locale:locale,cacheURL:cache,fetch:offline) }
        XCTAssertEqual(kept.code,0);XCTAssertEqual(kept.output,"1.00 USD = 30.00 TRY\n")
        XCTAssertEqual(CurrencyRateService.cached(at:cache),stale)
    }

    func testRateServiceDecodesCachesAndConverts() throws {
        let json=#"{"result":"success","base_code":"USD","time_last_update_unix":1793318551,"rates":{"USD":1,"AED":3.6725,"TRY":34.25}}"#
        let decoded=try CurrencyRateService.decode(Data(json.utf8))
        XCTAssertEqual(decoded.base,"USD")
        XCTAssertEqual(decoded.updated,Date(timeIntervalSince1970:1_793_318_551))
        XCTAssertEqual(try XCTUnwrap(decoded.convert(3.6725,from:"AED",to:"USD")),1,accuracy:1e-12)
        XCTAssertEqual(try XCTUnwrap(decoded.convert(1,from:"AED",to:"TRY")),34.25/3.6725,accuracy:1e-12)
        XCTAssertNil(decoded.convert(1,from:"USD",to:"EUR"))
        XCTAssertThrowsError(try CurrencyRateService.decode(Data(#"{"result":"error","base_code":"USD","time_last_update_unix":0,"rates":{}}"#.utf8)))
        XCTAssertThrowsError(try CurrencyRateService.decode(Data(#"{"result":"success","base_code":"USD","time_last_update_unix":0,"rates":{"AED":-1}}"#.utf8)))

        let directory=FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString,isDirectory:true)
        defer { try? FileManager.default.removeItem(at:directory) }
        let file=directory.appendingPathComponent("rates.json")
        XCTAssertNil(CurrencyRateService.cached(at:file))
        CurrencyRateService.store(decoded,at:file)
        XCTAssertEqual(CurrencyRateService.cached(at:file),decoded)
        XCTAssertFalse(CurrencyRateService.isStale(decoded,now:decoded.updated.addingTimeInterval(3600)))
        XCTAssertTrue(CurrencyRateService.isStale(decoded,now:decoded.updated.addingTimeInterval(3*86400)))
        XCTAssertTrue(CurrencyRateService.isStale(nil))
    }
}
