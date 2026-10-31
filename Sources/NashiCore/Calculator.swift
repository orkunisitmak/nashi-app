import Foundation

/// Inline answers for typed math, unit and currency queries: "10+10", "1 USD", "1 USD to TRY", "5 km in mi".
public enum Calculator {
    public enum Kind:String,Equatable,Sendable { case math,unit,currency }
    public struct Answer:Equatable,Sendable {
        public let kind:Kind
        public let value:Double
        /// Display text, including the target unit or currency code.
        public let text:String
        /// Plain number for the clipboard, without grouping separators.
        public let copyText:String
        /// The resolved query, e.g. "1 USD = 34.25 TRY".
        public let detail:String
        public let ratesUpdated:Date?
    }
    public enum Outcome:Equatable,Sendable {
        case answer(Answer)
        /// A currency query was recognized but no exchange rates are available yet.
        case needsRates(from:String,to:String)
    }

    public static func evaluate(_ input:String,rates:CurrencyRates?=nil,localCurrency:String="USD",locale:Locale = .current)->Outcome? {
        let query=input.trimmingCharacters(in:.whitespacesAndNewlines)
        guard !query.isEmpty,query.count<=512 else { return nil }
        let decimal=decimalSeparator(locale)
        if let conversion=parseConversion(query,localCurrency:localCurrency,rates:rates,decimal:decimal) { return convert(conversion,rates:rates,locale:locale) }
        var parser=Parser(query,decimal:decimal)
        guard let parsed=try? parser.parse(),parsed.value.isFinite,parsed.operations>0 else { return nil }
        let value=clean(parsed.value)
        return .answer(Answer(kind:.math,value:value,text:format(value,style:.math,locale:locale),copyText:copy(value,style:.math,locale:locale),detail:"\(query) =",ratesUpdated:nil))
    }

    public static let currencyPreferenceKey="calculatorCurrency"
    /// The currency bare amounts convert to: NASHI_CURRENCY, then Settings → Calculator, then the Mac's region.
    public static func localCurrency(environment:[String:String]=ProcessInfo.processInfo.environment,defaults:UserDefaults = .standard,locale:Locale = .current)->String {
        let isCode={(code:String)->Bool in code.count==3 && code.allSatisfy{$0.isASCII && $0.isLetter}}
        if let code=environment["NASHI_CURRENCY"]?.uppercased(),isCode(code) { return code }
        if let code=defaults.string(forKey:currencyPreferenceKey)?.uppercased(),isCode(code) { return code }
        return locale.currency?.identifier ?? "USD"
    }
    static func decimalSeparator(_ locale:Locale)->Character { locale.decimalSeparator == "," ? ",":"." }

    // MARK: Conversions

    private struct Conversion { let amount:Double;let from:Unit;let to:Unit;let amountText:String }
    private static let separators=try! NSRegularExpression(pattern:#"\s+(?:to|in|into|as|=)\s+|\s*(?:->|→)\s*"#,options:[.caseInsensitive])

    private static func parseConversion(_ query:String,localCurrency:String,rates:CurrencyRates?,decimal:Character)->Conversion? {
        let range=NSRange(query.startIndex...,in:query)
        if let match=separators.matches(in:query,range:range).last,let r=Range(match.range,in:query) {
            let left=String(query[..<r.lowerBound]),right=String(query[r.upperBound...]).trimmingCharacters(in:.whitespaces)
            guard let source=splitAmount(left,rates:rates,decimal:decimal) else { return nil }
            let targets=Unit.lookup(right,rates:rates)
            for from in source.units { if let to=targets.first(where:{$0.category==from.category}) { return Conversion(amount:source.amount,from:from,to:to,amountText:source.amountText) } }
            return nil
        }
        // A bare amount with a currency converts to the local currency, like "1 USD" or "$20".
        guard let source=splitAmount(query,rates:rates,decimal:decimal),let from=source.units.first,from.category == .currency,!source.amountText.isEmpty else { return nil }
        let local=localCurrency.uppercased()
        let target=from.id != local ? local:(local=="USD" ? "EUR":"USD")
        return Conversion(amount:source.amount,from:from,to:.currency(target),amountText:source.amountText)
    }

    /// Splits "12.5 km" or "$20" into an amount expression and the units its trailing word could mean.
    private static func splitAmount(_ text:String,rates:CurrencyRates?,decimal:Character)->(amount:Double,amountText:String,units:[Unit])? {
        var text=text.trimmingCharacters(in:.whitespaces)
        var prefix:[Unit]=[]
        if let first=text.first,let code=Unit.currencySymbols[first] { prefix=[.currency(code)];text=String(text.dropFirst()).trimmingCharacters(in:.whitespaces) }
        if !prefix.isEmpty {
            guard let value=amount(text,decimal:decimal) else { return nil }
            return (value,text,prefix)
        }
        // Prefer the longest unit phrase so "square feet" wins over "feet"; "5km" may omit the space.
        let words=text.split(separator:" ",omittingEmptySubsequences:true).map(String.init)
        var candidates:[(String,String)]=(1...max(1,min(3,words.count))).map{(words.dropLast($0).joined(separator:" "),words.suffix($0).joined(separator:" "))}
        if let last=words.last,let index=last.firstIndex(where:{!($0.isNumber || $0=="." || $0==",")}),index>last.startIndex {
            candidates.append(((words.dropLast()+[String(last[..<index])]).joined(separator:" "),String(last[index...])))
        }
        for (expression,unit) in candidates.sorted(by:{$0.1.count>$1.1.count}) {
            let units=Unit.lookup(unit,rates:rates)
            guard !units.isEmpty else { continue }
            if expression.trimmingCharacters(in:.whitespaces).isEmpty { return (1,"",units) }
            if let value=amount(expression,decimal:decimal) { return (value,expression.trimmingCharacters(in:.whitespaces),units) }
        }
        return nil
    }

    private static func amount(_ text:String,decimal:Character)->Double? {
        var parser=Parser(text,decimal:decimal)
        guard let parsed=try? parser.parse(),parsed.value.isFinite else { return nil }
        return parsed.value
    }

    private static func convert(_ conversion:Conversion,rates:CurrencyRates?,locale:Locale)->Outcome? {
        let from=conversion.from,to=conversion.to
        let amountLabel=conversion.amountText.isEmpty ? "1":format(conversion.amount,style:from.category == .currency ? .currency:.unit,locale:locale)
        if from.category == .currency {
            guard let rates else { return .needsRates(from:from.id,to:to.id) }
            // Downloaded rates that lack a code (e.g. BTC) will not gain it by fetching again.
            guard let value=rates.convert(conversion.amount,from:from.id,to:to.id) else { return nil }
            let text=format(value,style:.currency,locale:locale)
            return .answer(Answer(kind:.currency,value:value,text:"\(text) \(to.id)",copyText:copy(value,style:.currency,locale:locale),detail:"\(amountLabel) \(from.id) =",ratesUpdated:rates.updated))
        }
        guard let value=from.convert(conversion.amount,to:to),value.isFinite else { return nil }
        let cleaned=clean(value)
        return .answer(Answer(kind:.unit,value:cleaned,text:"\(format(cleaned,style:.unit,locale:locale)) \(to.symbol)",copyText:copy(cleaned,style:.unit,locale:locale),detail:"\(amountLabel) \(from.symbol) =",ratesUpdated:nil))
    }

    // MARK: Formatting

    enum Style { case math,unit,currency }
    /// Drops binary floating-point noise such as 0.1+0.2 = 0.30000000000000004.
    static func clean(_ value:Double)->Double {
        guard value != 0,value.isFinite else { return value == 0 ? 0:value }
        let digits=12-Int(floor(log10(abs(value))))
        guard digits>0,digits<300 else { return value }
        let scale=pow(10,Double(digits))
        let rounded=(value*scale).rounded()/scale
        return rounded.isFinite ? rounded:value
    }
    private static func formatter(_ style:Style,locale:Locale,grouping:Bool)->NumberFormatter {
        let formatter=NumberFormatter();formatter.locale=locale;formatter.numberStyle = .decimal;formatter.usesGroupingSeparator=grouping
        switch style {
        case .currency:formatter.minimumFractionDigits=2;formatter.maximumFractionDigits=2
        case .unit:formatter.maximumFractionDigits=6
        case .math:formatter.maximumFractionDigits=10
        }
        return formatter
    }
    private static func adjusted(_ value:Double,_ style:Style)->Style {
        // Tiny values keep their significant digits instead of rounding to zero.
        style == .currency && abs(value)<0.01 && value != 0 ? .math:style
    }
    static func format(_ value:Double,style:Style,locale:Locale)->String {
        if abs(value)>=1e15 || (abs(value)<1e-9 && value != 0) { return scientific(value,locale:locale) }
        return formatter(adjusted(value,style),locale:locale,grouping:true).string(from:NSNumber(value:value)) ?? String(value)
    }
    static func copy(_ value:Double,style:Style,locale:Locale)->String {
        if abs(value)>=1e15 || (abs(value)<1e-9 && value != 0) { return scientific(value,locale:locale) }
        return formatter(adjusted(value,style),locale:locale,grouping:false).string(from:NSNumber(value:value)) ?? String(value)
    }
    private static func scientific(_ value:Double,locale:Locale)->String {
        let formatter=NumberFormatter();formatter.locale=locale;formatter.numberStyle = .scientific;formatter.maximumFractionDigits=8
        return formatter.string(from:NSNumber(value:value)) ?? String(value)
    }
}

// MARK: Units

extension Calculator {
    enum Category:Equatable { case length,mass,volume,area,time,speed,data,temperature,angle,currency }
    struct Unit:Equatable {
        let id:String;let symbol:String;let category:Category;let factor:Double
        static func currency(_ code:String)->Unit { Unit(id:code,symbol:code,category:.currency,factor:1) }
        func convert(_ value:Double,to target:Unit)->Double? {
            guard category==target.category else { return nil }
            if category == .temperature { return Unit.fromKelvin(toKelvin(value),target.id) }
            return value*factor/target.factor
        }
        private func toKelvin(_ value:Double)->Double { id=="C" ? value+273.15:id=="F" ? (value-32)*5/9+273.15:value }
        private static func fromKelvin(_ value:Double,_ id:String)->Double { id=="C" ? value-273.15:id=="F" ? (value-273.15)*9/5+32:value }

        static let currencySymbols:[Character:String]=["$":"USD","€":"EUR","£":"GBP","¥":"JPY","₺":"TRY","₹":"INR","₽":"RUB","₩":"KRW"]
        static let currencyNames:[String:String]=["dollar":"USD","dollars":"USD","euro":"EUR","euros":"EUR","lira":"TRY","liras":"TRY","tl":"TRY","dirham":"AED","dirhams":"AED","yen":"JPY","rupee":"INR","rupees":"INR","riyal":"SAR","riyals":"SAR","franc":"CHF","francs":"CHF","sterling":"GBP","bitcoin":"BTC"]
        private static let currencyCodes=Set(Locale.commonISOCurrencyCodes)

        static func lookup(_ raw:String,rates:CurrencyRates?)->[Unit] {
            let text=raw.trimmingCharacters(in:CharacterSet.whitespaces.union(CharacterSet(charactersIn:".")))
            guard !text.isEmpty else { return [] }
            var result:[Unit]=[]
            if let unit=aliases[text] ?? aliases[text.lowercased()] { result.append(unit) }
            let upper=text.uppercased()
            if upper.count==3,upper.allSatisfy({$0.isLetter}),(rates?.rates[upper] != nil || currencyCodes.contains(upper)) { result.append(.currency(upper)) }
            if let code=currencyNames[text.lowercased()] { result.append(.currency(code)) }
            if text.count==1,let code=currencySymbols[text.first!] { result.append(.currency(code)) }
            return result
        }

        private static let definitions:[(Category,String,Double,[String])]=[
            (.length,"mm",0.001,["millimeter","millimeters","millimetre","millimetres"]),
            (.length,"cm",0.01,["centimeter","centimeters","centimetre","centimetres"]),
            (.length,"m",1,["meter","meters","metre","metres"]),
            (.length,"km",1000,["kilometer","kilometers","kilometre","kilometres","kms"]),
            (.length,"in",0.0254,["inch","inches","\""]),
            (.length,"ft",0.3048,["foot","feet","'"]),
            (.length,"yd",0.9144,["yard","yards"]),
            (.length,"mi",1609.344,["mile","miles"]),
            (.length,"nmi",1852,["nautical mile","nautical miles"]),
            (.mass,"mg",1e-6,["milligram","milligrams"]),
            (.mass,"g",0.001,["gram","grams","gr"]),
            (.mass,"kg",1,["kilogram","kilograms","kilo","kilos","kgs"]),
            (.mass,"t",1000,["tonne","tonnes","ton","tons","metric ton","metric tons"]),
            (.mass,"oz",0.028349523125,["ounce","ounces"]),
            (.mass,"lb",0.45359237,["lbs","pound","pounds"]),
            (.mass,"st",6.35029318,["stone","stones"]),
            (.volume,"ml",0.001,["milliliter","milliliters","millilitre","millilitres","mL"]),
            (.volume,"cl",0.01,["centiliter","centiliters","centilitre","centilitres"]),
            (.volume,"dl",0.1,["deciliter","deciliters","decilitre","decilitres"]),
            (.volume,"l",1,["L","liter","liters","litre","litres"]),
            (.volume,"m³",1000,["m3","cubic meter","cubic meters","cubic metre","cubic metres"]),
            (.volume,"tsp",0.00492892159375,["teaspoon","teaspoons"]),
            (.volume,"tbsp",0.01478676478125,["tablespoon","tablespoons"]),
            (.volume,"fl oz",0.0295735295625,["floz","fluid ounce","fluid ounces"]),
            (.volume,"cup",0.2365882365,["cups"]),
            (.volume,"pt",0.473176473,["pint","pints"]),
            (.volume,"qt",0.946352946,["quart","quarts"]),
            (.volume,"gal",3.785411784,["gallon","gallons"]),
            (.area,"mm²",1e-6,["mm2","square millimeter","square millimeters"]),
            (.area,"cm²",1e-4,["cm2","square centimeter","square centimeters"]),
            (.area,"m²",1,["m2","sqm","square meter","square meters","square metre","square metres"]),
            (.area,"km²",1e6,["km2","square kilometer","square kilometers","square kilometre","square kilometres"]),
            (.area,"ha",1e4,["hectare","hectares"]),
            (.area,"ac",4046.8564224,["acre","acres"]),
            (.area,"in²",0.00064516,["in2","sq in","square inch","square inches"]),
            (.area,"ft²",0.09290304,["ft2","sqft","sq ft","square foot","square feet"]),
            (.area,"yd²",0.83612736,["yd2","sq yd","square yard","square yards"]),
            (.area,"mi²",2589988.110336,["mi2","sq mi","square mile","square miles"]),
            (.time,"ms",0.001,["millisecond","milliseconds"]),
            (.time,"s",1,["sec","secs","second","seconds"]),
            (.time,"min",60,["mins","minute","minutes"]),
            (.time,"h",3600,["hr","hrs","hour","hours"]),
            (.time,"d",86400,["day","days"]),
            (.time,"wk",604800,["week","weeks"]),
            (.time,"mo",2629746,["month","months"]),
            (.time,"yr",31556952,["year","years"]),
            (.speed,"m/s",1,["mps","meters per second"]),
            (.speed,"km/h",1/3.6,["kmh","kph","kilometers per hour","kilometres per hour"]),
            (.speed,"mph",0.44704,["miles per hour","mi/h"]),
            (.speed,"kn",1852.0/3600,["knot","knots","kt"]),
            (.speed,"ft/s",0.3048,["fps","feet per second"]),
            (.data,"bit",0.125,["bits"]),
            (.data,"B",1,["byte","bytes"]),
            (.data,"KB",1e3,["kb","kilobyte","kilobytes"]),
            (.data,"MB",1e6,["mb","megabyte","megabytes"]),
            (.data,"GB",1e9,["gb","gigabyte","gigabytes"]),
            (.data,"TB",1e12,["tb","terabyte","terabytes"]),
            (.data,"KiB",1024,["kib","kibibyte","kibibytes"]),
            (.data,"MiB",1048576,["mib","mebibyte","mebibytes"]),
            (.data,"GiB",1073741824,["gib","gibibyte","gibibytes"]),
            (.data,"TiB",1099511627776,["tib","tebibyte","tebibytes"]),
            (.temperature,"°C",1,["C","c","celsius","centigrade","degc","degrees celsius"]),
            (.temperature,"°F",1,["F","f","fahrenheit","degf","degrees fahrenheit"]),
            (.temperature,"K",1,["k","kelvin","kelvins"]),
            (.angle,"°",Double.pi/180,["deg","degree","degrees"]),
            (.angle,"rad",1,["radian","radians"]),
        ]
        private static let aliases:[String:Unit]={
            var map:[String:Unit]=[:]
            for (category,symbol,factor,names) in definitions {
                let id=category == .temperature ? String(symbol.last!):symbol
                let unit=Unit(id:id,symbol:symbol,category:category,factor:factor)
                for name in [symbol]+names where map[name]==nil { map[name]=unit }
                if map[symbol.lowercased()]==nil { map[symbol.lowercased()]=unit }
            }
            return map
        }()
    }
}

// MARK: Expressions

extension Calculator {
    enum ParseError:Error { case invalid }
    /// Recursive-descent arithmetic: + - × ÷ ^ %, parentheses, "of", functions and constants.
    struct Parser {
        enum Token:Equatable { case number(Double),identifier(String),op(Character),open,close }
        private var tokens:[Token]=[]
        private var index=0
        private(set) var operations=0
        init(_ text:String,decimal:Character=".") { tokens=Parser.tokenize(text,decimal:decimal) }

        mutating func parse() throws->(value:Double,operations:Int) {
            guard !tokens.isEmpty else { throw ParseError.invalid }
            let value=try additive()
            guard index==tokens.count else { throw ParseError.invalid }
            return (value.value,operations)
        }

        private struct Value { var value:Double;var percent=false }
        private var peek:Token? { index<tokens.count ? tokens[index]:nil }

        private mutating func additive() throws->Value {
            var lhs=try multiplicative()
            while case .op(let op)? = peek,op=="+" || op=="-" {
                index+=1;operations+=1
                let rhs=try multiplicative()
                // "50 + 10%" means fifty plus ten percent of fifty.
                let amount=rhs.percent ? lhs.value*rhs.value:rhs.value
                lhs=Value(value:op=="+" ? lhs.value+amount:lhs.value-amount)
            }
            return lhs
        }
        private mutating func multiplicative() throws->Value {
            var lhs=try unary()
            while true {
                var op:Character
                switch peek {
                case .op(let symbol)? where "*/×÷".contains(symbol):op=symbol;index+=1
                case .identifier(let word)? where word=="of" || word=="mod":op=word=="of" ? "*":"m";index+=1
                case .open?,.identifier?:op="*" // Implicit multiplication, like 2(3+4) or 2pi.
                default:return lhs
                }
                operations+=1
                let rhs=try unary()
                switch op {
                case "*","×":lhs=Value(value:lhs.value*rhs.value)
                case "m":guard rhs.value != 0 else { throw ParseError.invalid };lhs=Value(value:lhs.value.truncatingRemainder(dividingBy:rhs.value))
                default:guard rhs.value != 0 else { throw ParseError.invalid };lhs=Value(value:lhs.value/rhs.value)
                }
            }
        }
        private mutating func unary() throws->Value {
            if case .op(let op)? = peek,op=="-" || op=="+" {
                index+=1
                let value=try unary()
                if op=="-" { operations+=1 }
                return Value(value:op=="-" ? -value.value:value.value,percent:value.percent)
            }
            return try power()
        }
        private mutating func power() throws->Value {
            let base=try postfix()
            guard case .op("^")? = peek else { return base }
            index+=1;operations+=1
            let exponent=try unary()
            return Value(value:pow(base.value,exponent.value))
        }
        private mutating func postfix() throws->Value {
            var value=try primary()
            while case .op(let op)? = peek,op=="%" || op=="!" {
                index+=1;operations+=1
                if op=="%" { value=Value(value:value.value/100,percent:true) }
                else {
                    guard value.value>=0,value.value<=170,value.value.rounded()==value.value else { throw ParseError.invalid }
                    value=Value(value:(1...max(1,Int(value.value))).reduce(1.0){$0*Double($1)})
                }
            }
            return value
        }
        private mutating func primary() throws->Value {
            guard let token=peek else { throw ParseError.invalid }
            index+=1
            switch token {
            case .number(let value):return Value(value:value)
            case .open:
                let value=try additive()
                guard peek == .close else { throw ParseError.invalid }
                index+=1;return Value(value:value.value)
            case .identifier(let name):
                if let constant=Parser.constants[name] { operations+=1;return Value(value:constant) }
                guard let function=Parser.functions[name] else { throw ParseError.invalid }
                if name=="√",peek != .open {
                    let value=sqrt(try postfix().value);operations+=1
                    guard value.isFinite else { throw ParseError.invalid }
                    return Value(value:value)
                }
                guard peek == .open else { throw ParseError.invalid }
                index+=1
                let argument=try additive()
                guard peek == .close else { throw ParseError.invalid }
                index+=1;operations+=1
                let value=function(argument.value)
                guard value.isFinite else { throw ParseError.invalid }
                return Value(value:value)
            default:throw ParseError.invalid
            }
        }

        static let constants:[String:Double]=["pi":Double.pi,"π":Double.pi,"e":M_E,"tau":2*Double.pi]
        static let functions:[String:(Double)->Double]=[
            "sqrt":{sqrt($0)},"√":{sqrt($0)},"cbrt":{cbrt($0)},"abs":{abs($0)},"round":{$0.rounded()},"floor":{floor($0)},"ceil":{ceil($0)},
            "sin":{sin($0)},"cos":{cos($0)},"tan":{tan($0)},"asin":{asin($0)},"acos":{acos($0)},"atan":{atan($0)},
            "ln":{log($0)},"log":{log10($0)},"log2":{log2($0)},"exp":{exp($0)}
        ]

        static func tokenize(_ text:String,decimal:Character=".")->[Token] { (try? tokenizeThrowing(text,decimal:decimal)) ?? [.op("?")] }
        /// Reads separators the way the locale writes numbers: "1.000,5" in Turkish, "1,000.5" in English.
        /// A lone separator that cannot be grouping ("1.5" in Turkish, "1,5" in English) still reads as a decimal point.
        static func number(_ raw:String,decimal:Character) throws->Double {
            let grouping:Character=decimal=="," ? ".":","
            var integer=raw,fraction=""
            if let index=raw.lastIndex(of:decimal) { integer=String(raw[..<index]);fraction=String(raw[raw.index(after:index)...]) }
            guard fraction.allSatisfy({$0.isASCII && $0.isNumber}) else { throw ParseError.invalid }
            let groups=integer.split(separator:grouping,omittingEmptySubsequences:false)
            if groups.count>1 {
                if !groups[0].isEmpty && groups[0].count<=3 && groups.dropFirst().allSatisfy({$0.count==3}) { integer=groups.joined() }
                else {
                    guard groups.count==2,fraction.isEmpty else { throw ParseError.invalid }
                    integer=String(groups[0]);fraction=String(groups[1])
                }
            }
            guard integer.allSatisfy({$0.isASCII && $0.isNumber}),let value=Double((integer.isEmpty ? "0":integer)+"."+(fraction.isEmpty ? "0":fraction)) else { throw ParseError.invalid }
            return value
        }
        private static func tokenizeThrowing(_ text:String,decimal:Character) throws->[Token] {
            var tokens:[Token]=[]
            let characters=Array(text)
            var i=0
            while i<characters.count {
                let c=characters[i]
                if c.isWhitespace { i+=1;continue }
                if c.isNumber || ((c=="." || c==",") && i+1<characters.count && characters[i+1].isNumber) {
                    // A separator belongs to the number only when a digit follows it.
                    var raw="";var j=i
                    while j<characters.count,characters[j].isNumber || ((characters[j]=="." || characters[j]==",") && j+1<characters.count && characters[j+1].isNumber) { raw.append(characters[j]);j+=1 }
                    tokens.append(.number(try number(raw,decimal:decimal)));i=j;continue
                }
                // "3x4" and "3 x (2+1)" read as multiplication.
                if c=="x" || c=="X",let last=tokens.last,last == .close || { if case .number = last { return true };return false }(),
                   let next=characters[(i+1)...].first(where:{!$0.isWhitespace}),next.isNumber || next=="(" { tokens.append(.op("*"));i+=1;continue }
                if c.isLetter || c=="π" || c=="√" {
                    var name="";var j=i
                    while j<characters.count,characters[j].isLetter || characters[j].isNumber && !name.isEmpty || characters[j]=="π" { name.append(characters[j]);j+=1 }
                    if c=="√" { name="√";j=i+1 }
                    tokens.append(.identifier(name.lowercased()));i=j;continue
                }
                switch c {
                case "(","[":tokens.append(.open)
                case ")","]":tokens.append(.close)
                case "−","–":tokens.append(.op("-"))
                case "*":
                    if i+1<characters.count,characters[i+1]=="*" { tokens.append(.op("^"));i+=1 } else { tokens.append(.op("*")) }
                case "+","-","/","×","÷","^","%","!":tokens.append(.op(c))
                default:throw ParseError.invalid
                }
                i+=1
            }
            return tokens
        }
    }
}

// MARK: Currency rates

public struct CurrencyRates:Codable,Equatable,Sendable {
    public let base:String
    public let rates:[String:Double]
    public let updated:Date
    public init(base:String,rates:[String:Double],updated:Date) { self.base=base;self.rates=rates;self.updated=updated }
    public func convert(_ amount:Double,from:String,to:String)->Double? {
        let rate={(code:String)->Double? in code==base ? 1:rates[code]}
        guard let source=rate(from),let target=rate(to),source>0 else { return nil }
        return amount/source*target
    }
    public var codes:[String] { Array(Set(rates.keys).union([base])).sorted() }
}

/// Fetched only when a typed query needs currency rates, then cached for offline use.
public enum CurrencyRateService {
    public static let endpoint=URL(string:"https://open.er-api.com/v6/latest/USD")!
    public static let attribution="Rates by ExchangeRate-API"
    public static let maximumAge:TimeInterval=12*3600
    public static var cacheURL:URL {
        let caches=FileManager.default.urls(for:.cachesDirectory,in:.userDomainMask).first ?? FileManager.default.temporaryDirectory
        return caches.appendingPathComponent("Nashi",isDirectory:true).appendingPathComponent("currency-rates.json")
    }
    public static func decode(_ data:Data)throws->CurrencyRates {
        struct Response:Decodable { let result:String;let base_code:String;let time_last_update_unix:TimeInterval;let rates:[String:Double] }
        let response=try JSONDecoder().decode(Response.self,from:data)
        guard response.result=="success",!response.rates.isEmpty,response.rates.values.allSatisfy({$0.isFinite && $0>0}) else { throw NashiError.invalidInput("The currency rate service returned no rates.") }
        return CurrencyRates(base:response.base_code.uppercased(),rates:response.rates,updated:Date(timeIntervalSince1970:response.time_last_update_unix))
    }
    public static func cached(at url:URL=cacheURL)->CurrencyRates? {
        guard let data=try? Data(contentsOf:url) else { return nil }
        return try? JSONDecoder().decode(CurrencyRates.self,from:data)
    }
    public static func store(_ rates:CurrencyRates,at url:URL=cacheURL) {
        try? FileManager.default.createDirectory(at:url.deletingLastPathComponent(),withIntermediateDirectories:true)
        if let data=try? JSONEncoder().encode(rates) { try? data.write(to:url,options:.atomic) }
    }
    public static func isStale(_ rates:CurrencyRates?,now:Date=Date())->Bool {
        guard let rates else { return true }
        return now.timeIntervalSince(rates.updated)>maximumAge*2
    }
    public static func fetch(session:URLSession=URLSession(configuration:.ephemeral))async throws->CurrencyRates {
        var request=URLRequest(url:endpoint,cachePolicy:.reloadIgnoringLocalCacheData,timeoutInterval:15)
        request.setValue("application/json",forHTTPHeaderField:"Accept")
        let (data,response)=try await session.data(for:request)
        guard (response as? HTTPURLResponse)?.statusCode==200 else { throw NashiError.invalidInput("Currency rates are unavailable right now.") }
        return try decode(data)
    }
}
