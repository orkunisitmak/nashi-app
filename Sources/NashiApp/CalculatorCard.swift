import AppKit
import SwiftUI
import NashiCore

/// Home's inline calculator. Typed math answers immediately; currency rates are fetched only when a query needs them.
@MainActor final class CalculatorModel:ObservableObject {
    @Published var query="" { didSet { if query != oldValue { copied=false;refresh() } } }
    @Published private(set) var outcome:Calculator.Outcome?
    @Published private(set) var loadingRates=false
    @Published private(set) var ratesUnavailable=false
    @Published private(set) var copied=false
    private var rates:CurrencyRates?
    private var fetchTask:Task<Void,Never>?
    private var lastAttempt:Date?
    init(rates:CurrencyRates?=CurrencyRateService.cached(),query:String="") { self.rates=rates;self.query=query;refresh() }

    static var localCurrency:String { Calculator.localCurrency(environment:[:]) }
    var answer:Calculator.Answer? { if case .answer(let answer)? = outcome { return answer };return nil }

    func refresh() {
        outcome=Calculator.evaluate(query,rates:rates,localCurrency:Self.localCurrency)
        switch outcome {
        case .needsRates?:fetchRates(force:false)
        case .answer(let answer)? where answer.kind == .currency && CurrencyRateService.isStale(rates):fetchRates(force:false)
        default:break
        }
    }
    func fetchRates(force:Bool) {
        // Avoid a request per keystroke when the service is unreachable or still serving yesterday's rates.
        guard fetchTask == nil else { return }
        if !force,let lastAttempt,Date().timeIntervalSince(lastAttempt)<600 { return }
        lastAttempt=Date();loadingRates=true;ratesUnavailable=false
        fetchTask=Task { [weak self] in
            let fresh=try? await CurrencyRateService.fetch()
            guard let self else { return }
            if let fresh { self.rates=fresh;CurrencyRateService.store(fresh) }
            self.ratesUnavailable=self.rates == nil
            self.loadingRates=false;self.fetchTask=nil
            self.outcome=Calculator.evaluate(self.query,rates:self.rates,localCurrency:Self.localCurrency)
        }
    }
    @discardableResult func copyAnswer(pasteboard:NSPasteboard = .general)->Bool {
        guard let answer else { return false }
        pasteboard.clearContents();pasteboard.setString(answer.copyText,forType:.string)
        copied=true;return true
    }
}

struct CalculatorCard:View {
    @ObservedObject var model:CalculatorModel
    @AppStorage("appLanguage") private var language="en"
    @AppStorage(Calculator.currencyPreferenceKey) private var currency="auto"
    var body:some View {
        VStack(alignment:.leading,spacing:12) {
            HStack(spacing:12) {
                Image(systemName:"equal.circle.fill").font(.system(size:25)).foregroundStyle(NashiPalette.forest)
                VStack(alignment:.leading,spacing:4) {
                    TextField("Calculate or convert",text:$model.query,prompt:Text("10+10 · 1 USD to TRY · 5 km in mi"))
                        .textFieldStyle(.plain).font(.system(size:15,weight:.semibold,design:.rounded))
                        .onSubmit { model.copyAnswer() }.accessibilityIdentifier("Calculator").accessibilityLabel("Calculate or convert")
                    if model.query.isEmpty { Text("Math, units, and currencies. Answers appear as you type.").font(.caption).foregroundStyle(.secondary) }
                }
                Spacer(minLength:8)
                if !model.query.isEmpty {
                    Button { model.query="" } label:{Image(systemName:"xmark.circle.fill").font(.system(size:14))}
                        .buttonStyle(.plain).foregroundStyle(.tertiary).help("Clear").accessibilityLabel("Clear calculation")
                }
            }
            if let answer=model.answer { result(answer).transition(.opacity.combined(with:.move(edge:.top))) }
            else if case .needsRates(let from,let to)? = model.outcome { pendingRates(from:from,to:to).transition(.opacity) }
        }.padding(18).frame(maxWidth:.infinity,alignment:.leading).nashiCard()
            .nashiAnimation(value:model.answer).nashiAnimation(value:model.loadingRates)
            .onChange(of:currency) { _ in model.refresh() }
    }
    private func result(_ answer:Calculator.Answer)->some View {
        VStack(alignment:.leading,spacing:10) {
            Divider()
            HStack(alignment:.firstTextBaseline,spacing:12) {
                VStack(alignment:.leading,spacing:3) {
                    Text(verbatim:answer.detail).font(.system(size:11,weight:.medium)).foregroundStyle(.secondary).lineLimit(1)
                    Text(verbatim:answer.text).font(.system(size:26,weight:.bold,design:.rounded)).tracking(-0.5).monospacedDigit()
                        .lineLimit(1).minimumScaleFactor(0.5).textSelection(.enabled).accessibilityIdentifier("CalculatorAnswer")
                }
                Spacer(minLength:8)
                Button { model.copyAnswer() } label:{Label(model.copied ? "Copied":"Copy",systemImage:model.copied ? "checkmark":"doc.on.doc")}
                    .buttonStyle(NashiButtonStyle(.secondary,compact:true)).help("Return copies the answer")
            }
            if answer.kind == .currency,let updated=answer.ratesUpdated {
                HStack(spacing:4) {
                    LText("Exchange rates: \(updated.formatted(Date.FormatStyle(date:.abbreviated,time:.shortened).locale(Locale(identifier:language))))")
                    Text(verbatim:"·")
                    Link("ExchangeRate-API",destination:URL(string:"https://www.exchangerate-api.com")!)
                    if model.loadingRates { ProgressView().controlSize(.mini).padding(.leading,2) }
                }.font(.caption2).foregroundStyle(.secondary)
            }
        }
    }
    private func pendingRates(from:String,to:String)->some View {
        VStack(alignment:.leading,spacing:10) {
            Divider()
            HStack(spacing:9) {
                if model.loadingRates { ProgressView().controlSize(.small);LText("Getting today’s exchange rates for \(from) → \(to)…") }
                else if model.ratesUnavailable {
                    Image(systemName:"wifi.exclamationmark").foregroundStyle(.orange)
                    Text("Exchange rates are unavailable. Check your connection and try again.")
                    Spacer(minLength:8)
                    Button("Try again") { model.fetchRates(force:true) }.buttonStyle(NashiButtonStyle(.secondary,compact:true))
                }
            }.font(.system(size:12)).foregroundStyle(.secondary)
        }
    }
}
