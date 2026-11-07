namespace Lignum.Core.Syntax;

/// <summary>
/// The schemes Lignum ships with (ticket 25), also what "Factory defaults" brings back. Each call returns new
/// objects. Lines are colored on their own, so block comments and multi-line strings are colored only on the
/// lines where their markers are; C-style doc comment lines that start with <c>*</c> are colored too.
/// </summary>
public static class BuiltInSyntaxSchemes
{
    /// <summary>Token kinds, each with a light and a dark color (VS Code's default palettes).</summary>
    private enum Kind { Comment, String, Number, Keyword, Control, Type, Function, Preprocessor, Tag, Attribute, Key, Variable, Constant, Heading, Added, Removed, Punctuation, Escape }

    private static (string Light, string Dark, bool Bold, bool Italic) Style(Kind kind) => kind switch
    {
        Kind.Comment => ("#008000", "#6A9955", false, false),
        Kind.String => ("#A31515", "#CE9178", false, false),
        Kind.Number => ("#098658", "#B5CEA8", false, false),
        Kind.Keyword => ("#0000FF", "#569CD6", false, false),
        Kind.Control => ("#AF00DB", "#C586C0", false, false),
        Kind.Type => ("#267F99", "#4EC9B0", false, false),
        Kind.Function => ("#795E26", "#DCDCAA", false, false),
        Kind.Preprocessor => ("#808080", "#9B9B9B", false, false),
        Kind.Tag => ("#800000", "#569CD6", false, false),
        Kind.Attribute => ("#E50000", "#9CDCFE", false, false),
        Kind.Key => ("#0451A5", "#9CDCFE", false, false),
        Kind.Variable => ("#001080", "#9CDCFE", false, false),
        Kind.Constant => ("#0070C1", "#4FC1FF", false, false),
        Kind.Heading => ("#800000", "#569CD6", true, false),
        Kind.Added => ("#098658", "#73C991", false, false),
        Kind.Removed => ("#A31515", "#F14C4C", false, false),
        Kind.Punctuation => ("#AF00DB", "#C586C0", true, false),
        Kind.Escape => ("#EE0000", "#D7BA7D", false, false),
        _ => ("#000000", "#D4D4D4", false, false),
    };

    // ---- Shared patterns -------------------------------------------------------------------------------------

    private const string LineCommentSlash = "//.*";
    private const string LineCommentHash = "#.*";
    private const string BlockComment = @"/\*.*?(?:\*/|$)";
    private const string DocCommentLine = @"^\s*\*(?!/).*"; // " * text" inside /** … */
    private const string BlockCommentEnd = @"^[^/""'\n]*?\*/"; // "… */" closing a block comment
    private const string DoubleQuoted = @"""(?:[^""\\]|\\.)*""?";
    private const string SingleQuoted = @"'(?:[^'\\]|\\.)*'?";
    private const string CharLiteral = @"'(?:\\.|[^'\\])+'";
    private const string Backtick = @"`(?:[^`\\]|\\.)*`?";
    private const string Number = @"\b(?:0[xX][0-9a-fA-F_]+|0[bB][01_]+|\d[\d_]*(?:\.\d[\d_]*)?(?:[eE][+-]?\d+)?)[uUlLfFdDmMnZz]*\b";
    private const string PascalCaseType = @"\b[A-Z][A-Za-z0-9_]*\b";
    // A PascalCase word used as a type: before generics, nullable, array, a member access or a declared name.
    private const string TypeUse = @"\b[A-Z][A-Za-z0-9_]*(?=<|\?[\s>,)]|\[\]|\.[A-Za-z_]|\s+@?[A-Za-z_]\w*\s*(?:[=;,)(:{]|$))";
    private const string FunctionCall =@"\b[A-Za-z_]\w*(?=\s*\()";
    private const string Annotation = @"@[A-Za-z_][\w.]*";

    private static string Words(string words) =>
        @"\b(?:" + string.Join("|", words.Split(' ', StringSplitOptions.RemoveEmptyEntries)) + @")\b";

    private static SyntaxRule R(string name, string pattern, Kind kind, bool matchCase = true)
    {
        var (light, dark, bold, italic) = Style(kind);
        return new SyntaxRule
        {
            Name = name, Pattern = pattern, MatchCase = matchCase,
            LightColor = light, DarkColor = dark, Bold = bold, Italic = italic,
        };
    }

    private static SyntaxScheme S(string name, string extensions, params SyntaxRule[] rules) => new()
    {
        Name = name,
        Extensions = SyntaxFileTypes.ParseExtensions(extensions),
        Rules = [.. rules],
    };

    /// <summary>Comments, strings, numbers, keywords, types and calls for a language with C-style syntax.</summary>
    private static SyntaxScheme CLike(string name, string extensions, string keywords, string control, string types,
                                      SyntaxRule[]? strings = null, SyntaxRule[]? extra = null,
                                      bool pascalTypes = true, bool charLiterals = true)
    {
        var rules = new List<SyntaxRule>
        {
            R("Comment", LineCommentSlash, Kind.Comment),
            R("Block comment", BlockComment, Kind.Comment),
            R("Doc comment line", DocCommentLine, Kind.Comment),
            R("Block comment end", BlockCommentEnd, Kind.Comment),
        };
        rules.AddRange(strings ?? []);
        rules.Add(R("String", DoubleQuoted, Kind.String));
        if (charLiterals) rules.Add(R("Character", CharLiteral, Kind.String));
        rules.AddRange(extra ?? []);
        rules.Add(R("Number", Number, Kind.Number));
        rules.Add(R("Control keyword", Words(control), Kind.Control));
        rules.Add(R("Keyword", Words(keywords), Kind.Keyword));
        if (types.Length > 0) rules.Add(R("Built-in type", Words(types), Kind.Keyword));
        rules.Add(R("Function", FunctionCall, Kind.Function));
        if (pascalTypes) rules.Add(R("Type", TypeUse, Kind.Type));
        return S(name, extensions, [.. rules]);
    }

    private const string CControl = "if else switch case default for while do break continue return goto try catch finally throw";

    /// <summary>All built-in schemes, in the order the editor lists them.</summary>
    public static List<SyntaxScheme> Create() =>
    [
        CLike("C#", ".cs .csx .cake",
            "abstract as base class const delegate enum event explicit extern false fixed implicit in interface internal is lock namespace new null operator out override params private protected public readonly record ref sealed sizeof stackalloc static struct this true typeof unchecked checked unsafe using virtual volatile async await var get set init value where when yield global partial nameof required file scoped with and or not managed",
            CControl + " foreach",
            "bool byte char decimal double float int long object sbyte short string uint ulong ushort void dynamic nint nuint",
            strings: [R("Raw string", "\"\"\".*?(?:\"\"\"|$)", Kind.String), R("Verbatim string", "\\$?@\\$?\"(?:[^\"]|\"\")*\"?", Kind.String)],
            extra: [R("Preprocessor", @"^\s*#\s*\w+.*", Kind.Preprocessor), R("Attribute", @"(?<=\[)\s*[A-Z]\w*(?=[\](])", Kind.Type)]),

        CLike("C / C++", ".c .h .cpp .hpp .cc .cxx .hh .hxx .c++ .h++ .ino .inl .ipp .tpp .cu .cuh",
            "alignas alignof asm auto class concept const consteval constexpr constinit const_cast decltype delete dynamic_cast enum explicit export extern false friend inline mutable namespace new noexcept nullptr operator private protected public register reinterpret_cast requires sizeof static static_assert static_cast struct template this thread_local true typedef typeid typename union using virtual volatile NULL override final co_await co_return co_yield",
            CControl,
            "bool char char8_t char16_t char32_t double float int long short signed unsigned void wchar_t size_t ssize_t int8_t int16_t int32_t int64_t uint8_t uint16_t uint32_t uint64_t intptr_t uintptr_t ptrdiff_t",
            strings: [R("Raw string", @"R""([^(]*)\(.*?\)\1""", Kind.String)],
            extra: [R("Preprocessor", @"^\s*#\s*\w+.*", Kind.Preprocessor)],
            pascalTypes: false),

        CLike("Java", ".java .jav",
            "abstract assert class const enum extends final implements import instanceof interface native new package private protected public static strictfp super synchronized this throws transient volatile var record sealed permits non-sealed yield true false null",
            CControl,
            "boolean byte char double float int long short void",
            strings: [R("Text block", "\"\"\".*?(?:\"\"\"|$)", Kind.String)],
            extra: [R("Annotation", Annotation, Kind.Type)]),

        CLike("Kotlin", ".kt .kts",
            "abstract actual annotation as by class companion const constructor crossinline data enum expect external false final fun get import in infix init inline inner interface internal is lateinit noinline null object open operator out override package private protected public reified sealed set super suspend tailrec this true typealias val var vararg where",
            "if else when for while do break continue return try catch finally throw",
            "Any Boolean Byte Char Double Float Int Long Nothing Short String Unit",
            strings: [R("Raw string", "\"\"\".*?(?:\"\"\"|$)", Kind.String)],
            extra: [R("Annotation", Annotation, Kind.Type), R("Template", @"\$\{[^}]*\}|\$\w+", Kind.Variable)]),

        CLike("Go", ".go",
            "chan const defer func go import interface map package range select struct type var true false nil iota",
            "if else switch case default for break continue return goto fallthrough",
            "bool byte complex64 complex128 error float32 float64 int int8 int16 int32 int64 rune string uint uint8 uint16 uint32 uint64 uintptr any comparable",
            strings: [R("Raw string", "`[^`]*`?", Kind.String)],
            pascalTypes: false),

        CLike("Rust", ".rs",
            "as async await const crate dyn enum extern false fn impl in let mod move mut pub ref self Self static struct super trait true type unsafe use where union",
            "if else match for while loop break continue return",
            "bool char f32 f64 i8 i16 i32 i64 i128 isize str u8 u16 u32 u64 u128 usize String Vec Option Result Box Some None Ok Err",
            strings: [R("Raw string", "r#*\".*?(?:\"#*|$)", Kind.String)],
            extra: [R("Attribute", @"#!?\[[^\]]*\]?", Kind.Preprocessor), R("Macro", @"\b\w+!", Kind.Function), R("Lifetime", @"'[a-z_]\w*\b(?!')", Kind.Type)]),

        CLike("Swift", ".swift",
            "associatedtype class deinit enum extension fileprivate func import init inout internal let open operator private precedencegroup protocol public rethrows static struct subscript typealias var async await actor some any as is nil true false self Self super throws mutating nonmutating override weak unowned lazy final required convenience dynamic indirect",
            "if else guard switch case default for in while repeat break continue fallthrough return throw do try catch defer where",
            "Int Double Float Bool String Character Void Any",
            strings: [R("Multi-line string", "\"\"\".*?(?:\"\"\"|$)", Kind.String)],
            extra: [R("Attribute", Annotation, Kind.Type), R("Directive", @"^\s*#\w+.*", Kind.Preprocessor)]),

        CLike("JavaScript", ".js .mjs .cjs .jsx",
            "async await class const debugger delete export extends false function get import in instanceof let new null of set static super this true typeof undefined var void yield from as NaN Infinity",
            CControl,
            "",
            strings: [R("Template string", Backtick, Kind.String), R("String", SingleQuoted, Kind.String)],
            charLiterals: false),

        CLike("TypeScript", ".ts .tsx .mts .cts",
            "abstract as async await class const constructor declare debugger delete enum export extends false function get implements import in infer instanceof interface is keyof let module namespace new null of override private protected public readonly require satisfies set static super this true type typeof undefined unique var void yield from",
            CControl,
            "any bigint boolean never number object string symbol unknown",
            strings: [R("Template string", Backtick, Kind.String), R("String", SingleQuoted, Kind.String)],
            extra: [R("Decorator", Annotation, Kind.Function)],
            charLiterals: false),

        CLike("Scala", ".scala .sc .sbt",
            "abstract case class def extends false final forSome given implicit import lazy new null object override package private protected sealed super this trait true type val var with using enum then export extension end opaque inline transparent",
            "if else match for while do yield return try catch finally throw",
            "Unit Int Long Double Float Boolean Char String Byte Short Any AnyRef Nothing",
            strings: [R("Multi-line string", "\"\"\".*?(?:\"\"\"|$)", Kind.String)],
            extra: [R("Annotation", Annotation, Kind.Type)]),

        CLike("Groovy / Gradle", ".groovy .gvy .gradle .jenkinsfile Jenkinsfile",
            "abstract as assert class def enum extends false final implements import in instanceof interface new null package private protected public static super this throws trait true var",
            CControl,
            "boolean byte char double float int long short void",
            strings: [R("Triple-quoted string", "(?:'''|\"\"\").*?(?:'''|\"\"\"|$)", Kind.String), R("String", SingleQuoted, Kind.String)],
            extra: [R("Annotation", Annotation, Kind.Type)],
            charLiterals: false),

        CLike("Dart", ".dart",
            "abstract as assert async await base class const covariant deferred dynamic enum export extends extension external factory false final get hide implements import in interface is late library mixin new null of on operator part required sealed set show static super sync this true typedef var with yield",
            CControl + " rethrow",
            "void int double num bool String List Map Set Object Future Stream",
            strings: [R("Triple-quoted string", "(?:'''|\"\"\").*?(?:'''|\"\"\"|$)", Kind.String), R("String", SingleQuoted, Kind.String)],
            extra: [R("Annotation", Annotation, Kind.Type)],
            charLiterals: false),

        CLike("PHP", ".php .phtml .php3 .php4 .php5 .phps",
            "abstract and array as callable class clone const declare echo enum extends false final fn function global implements include include_once instanceof insteadof interface isset list match namespace new null or print private protected public readonly require require_once static this trait true unset use var xor yield",
            CControl + " foreach elseif endif endforeach endwhile",
            "int float bool string void mixed object iterable never",
            strings: [R("String", SingleQuoted, Kind.String), R("Hash comment", @"#(?!\[).*", Kind.Comment)],
            extra: [R("Tag", @"<\?(?:php|=)?|\?>", Kind.Preprocessor), R("Variable", @"\$\w+", Kind.Variable)],
            pascalTypes: false, charLiterals: false),

        CLike("Protocol Buffers", ".proto",
            "syntax package import option message enum service rpc returns stream oneof map reserved extend extensions to max optional required repeated public weak true false",
            "",
            "double float int32 int64 uint32 uint64 sint32 sint64 fixed32 fixed64 sfixed32 sfixed64 bool string bytes",
            strings: [R("String", SingleQuoted, Kind.String)],
            charLiterals: false),

        S("Python", ".py .pyw .pyi .pyx .gyp",
            R("Comment", LineCommentHash, Kind.Comment),
            R("Triple-quoted string", @"[rRbBuUfF]{0,2}(?:'''|"""""").*?(?:'''|""""""|$)", Kind.String),
            R("String", @"[rRbBuUfF]{0,2}" + DoubleQuoted, Kind.String),
            R("String", @"[rRbBuUfF]{0,2}" + SingleQuoted, Kind.String),
            R("Decorator", Annotation, Kind.Function),
            R("Number", Number, Kind.Number),
            R("Control keyword", Words("if elif else for while break continue return try except finally raise with yield pass match case async await"), Kind.Control),
            R("Keyword", Words("and as assert class def del from global import in is lambda nonlocal not or True False None self cls print"), Kind.Keyword),
            R("Built-in", Words("int float str bool list dict set tuple bytes object type len range open isinstance super"), Kind.Type),
            R("Type", PascalCaseType, Kind.Type),
            R("Function", FunctionCall, Kind.Function)),

        S("Ruby", ".rb .rake .gemspec .ru .erb Gemfile Rakefile Vagrantfile Podfile",
            R("Comment", LineCommentHash, Kind.Comment),
            R("Doc block", @"^=(?:begin|end)\b.*", Kind.Comment),
            R("String", DoubleQuoted, Kind.String),
            R("String", SingleQuoted, Kind.String),
            R("Symbol", @"(?<![:\w]):\w+[?!]?", Kind.Constant),
            R("Variable", @"[@$]{1,2}\w+", Kind.Variable),
            R("Number", Number, Kind.Number),
            R("Control keyword", Words("if elsif else unless case when while until for break next redo retry return begin rescue ensure raise yield then do end"), Kind.Control),
            R("Keyword", Words("alias and class def defined module nil not or self super true false undef require require_relative include extend attr_accessor attr_reader attr_writer private protected public lambda proc puts"), Kind.Keyword),
            R("Type", PascalCaseType, Kind.Type)),

        S("Perl", ".pl .pm .t .pod",
            R("Comment", LineCommentHash, Kind.Comment),
            R("Pod", @"^=\w+.*", Kind.Comment),
            R("String", DoubleQuoted, Kind.String),
            R("String", SingleQuoted, Kind.String),
            R("Variable", @"[$@%]\{?\w+\}?", Kind.Variable),
            R("Number", Number, Kind.Number),
            R("Control keyword", Words("if elsif else unless while until for foreach last next redo return die"), Kind.Control),
            R("Keyword", Words("my our local sub package use require no print printf say defined undef eq ne lt gt le ge and or not qw"), Kind.Keyword),
            R("Function", FunctionCall, Kind.Function)),

        S("Lua", ".lua",
            R("Block comment", @"--\[=*\[.*?(?:\]=*\]|$)", Kind.Comment),
            R("Comment", "--.*", Kind.Comment),
            R("Long string", @"\[=*\[.*?(?:\]=*\]|$)", Kind.String),
            R("String", DoubleQuoted, Kind.String),
            R("String", SingleQuoted, Kind.String),
            R("Number", Number, Kind.Number),
            R("Control keyword", Words("if then elseif else for in while do repeat until break return goto end"), Kind.Control),
            R("Keyword", Words("and function local nil not or true false self"), Kind.Keyword),
            R("Function", FunctionCall, Kind.Function)),

        S("R", ".r .rprofile",
            R("Comment", LineCommentHash, Kind.Comment),
            R("String", DoubleQuoted, Kind.String),
            R("String", SingleQuoted, Kind.String),
            R("Number", Number, Kind.Number),
            R("Assignment", "<<?-|->>?", Kind.Keyword),
            R("Control keyword", Words("if else for while repeat break next return switch"), Kind.Control),
            R("Keyword", Words("function in TRUE FALSE NULL NA NaN Inf library require"), Kind.Keyword),
            R("Function", FunctionCall, Kind.Function)),

        S("Haskell", ".hs .lhs",
            R("Block comment", @"\{-.*?(?:-\}|$)", Kind.Comment),
            R("Comment", @"--(?![!#$%&*+./<=>?@\\^|~]).*", Kind.Comment),
            R("String", DoubleQuoted, Kind.String),
            R("Character", CharLiteral, Kind.String),
            R("Number", Number, Kind.Number),
            R("Keyword", Words("case class data default deriving do else family forall foreign hiding if import in infix infixl infixr instance let mdo module newtype of proc qualified rec then type where"), Kind.Keyword),
            R("Type", PascalCaseType, Kind.Type)),

        S("Elixir", ".ex .exs .heex",
            R("Comment", LineCommentHash, Kind.Comment),
            R("Doc string", "\"\"\".*?(?:\"\"\"|$)", Kind.String),
            R("String", DoubleQuoted, Kind.String),
            R("Atom", @"(?<![:\w]):\w+[?!]?", Kind.Constant),
            R("Attribute", @"@\w+", Kind.Variable),
            R("Number", Number, Kind.Number),
            R("Control keyword", Words("if else unless case cond with for receive try catch rescue after raise do end fn"), Kind.Control),
            R("Keyword", Words("def defp defmodule defmacro defstruct defprotocol defimpl alias import require use when and or not in true false nil"), Kind.Keyword),
            R("Module", PascalCaseType, Kind.Type),
            R("Function", FunctionCall, Kind.Function)),

        S("F#", ".fs .fsi .fsx .fsscript",
            R("Comment", LineCommentSlash, Kind.Comment),
            R("Block comment", @"\(\*(?!\)).*?(?:\*\)|$)", Kind.Comment),
            R("Triple-quoted string", "\"\"\".*?(?:\"\"\"|$)", Kind.String),
            R("String", DoubleQuoted, Kind.String),
            R("Character", CharLiteral, Kind.String),
            R("Attribute", @"\[<[^>]*>\]", Kind.Type),
            R("Number", Number, Kind.Number),
            R("Control keyword", Words("if then elif else match with for to downto in while do done yield return try finally raise"), Kind.Control),
            R("Keyword", Words("abstract and as assert base begin class default delegate end exception extern false fun function global inherit inline interface internal lazy let member module mutable namespace new not null of open or override private public rec static struct true type upcast downcast use val void when"), Kind.Keyword),
            R("Type", PascalCaseType, Kind.Type)),

        S("Visual Basic", ".vb .vbs .bas .vba .cls .frm",
            R("Comment", @"(?:'|\bREM\b).*", Kind.Comment, matchCase: false),
            R("String", @"""(?:[^""]|"""")*""?", Kind.String),
            R("Preprocessor", @"^\s*#\w+.*", Kind.Preprocessor),
            R("Number", Number, Kind.Number),
            R("Control keyword", Words("If Then Else ElseIf End Select Case For Each Next To Step While Wend Do Loop Until Exit Return GoTo Try Catch Finally Throw"), Kind.Control, matchCase: false),
            R("Keyword", Words("AddHandler AddressOf And AndAlso As ByRef ByVal Call Class Const Declare Dim Enum Event False Function Get Handles Implements Imports In Inherits Interface Is IsNot Let Me Module MustInherit MustOverride MyBase New Not Nothing Of On Option Optional Or OrElse Overloads Overridable Overrides ParamArray Partial Private Property Protected Public ReadOnly ReDim Set Shadows Shared Static Structure Sub True Using With WithEvents WriteOnly"), Kind.Keyword, matchCase: false),
            R("Built-in type", Words("Boolean Byte Char Date Decimal Double Integer Long Object Short Single String Variant"), Kind.Keyword, matchCase: false)),

        S("PowerShell", ".ps1 .psm1 .psd1 .ps1xml",
            R("Block comment", "<#.*?(?:#>|$)", Kind.Comment),
            R("Block comment end", @"^[^#""']*#>", Kind.Comment),
            R("Comment", LineCommentHash, Kind.Comment),
            R("Here-string", @"^\s*[""']?@\s*$|@[""']\s*$", Kind.String),
            R("String", @"""(?:[^""`]|`.)*""?", Kind.String),
            R("String", @"'(?:[^']|'')*'?", Kind.String),
            R("Variable", @"\$(?:\{[^}]*\}|[\w:?]+)", Kind.Variable),
            R("Type", @"\[[\w.]+(?:\[\])?\]", Kind.Type),
            R("Number", Number, Kind.Number),
            R("Operator", @"-(?:eq|ne|gt|ge|lt|le|like|notlike|match|notmatch|contains|notcontains|in|notin|replace|split|join|and|or|not|xor|is|isnot|as|f)\b", Kind.Keyword, matchCase: false),
            R("Control keyword", Words("if elseif else switch foreach for while do until break continue return exit throw try catch finally trap"), Kind.Control, matchCase: false),
            R("Keyword", Words("begin process end filter function param dynamicparam class enum using hidden static data in"), Kind.Keyword, matchCase: false),
            R("Cmdlet", @"\b[A-Z][a-z]+-[A-Z]\w+\b", Kind.Function)),

        S("Shell", ".sh .bash .zsh .ksh .fish .bashrc .zshrc .profile .bash_profile",
            R("Comment", @"(?<![\w$\\{])#.*", Kind.Comment),
            R("String", DoubleQuoted, Kind.String),
            R("String", @"'[^']*'?", Kind.String),
            R("Command substitution", Backtick, Kind.String),
            R("Variable", @"\$(?:\{[^}]*\}|\w+|[@*#?$!0-9-])", Kind.Variable),
            R("Number", @"\b\d+\b", Kind.Number),
            R("Control keyword", Words("if then elif else fi case esac for select while until do done in break continue return exit function"), Kind.Control),
            R("Builtin", Words("alias bg cd command declare echo eval exec export false fg getopts hash jobs kill let local printf pwd read readonly set shift source test time trap true type typeset ulimit umask unalias unset wait"), Kind.Keyword)),

        S("Batch", ".bat .cmd .btm",
            R("Comment", @"^\s*@?(?:rem\b|::).*", Kind.Comment, matchCase: false),
            R("Label", @"^\s*:\w+", Kind.Function),
            R("String", @"""[^""]*""?", Kind.String),
            R("Variable", @"%%?~?\w+%?|![\w]+!", Kind.Variable),
            R("Keyword", Words("if else for in do goto call exit echo set setlocal endlocal pushd popd shift start cd chdir md mkdir rd rmdir del erase copy move ren type not exist defined errorlevel equ neq lss leq gtr geq nul off on"), Kind.Keyword, matchCase: false)),

        S("SQL", ".sql .ddl .dml .pgsql .psql .plsql",
            R("Comment", "--.*", Kind.Comment),
            R("Block comment", BlockComment, Kind.Comment),
            R("Block comment end", BlockCommentEnd, Kind.Comment),
            R("String", @"[NnEe]?'(?:[^']|'')*'?", Kind.String),
            R("Quoted name", @"""[^""]*""|\[[^\]]*\]|`[^`]*`", Kind.Variable),
            R("Variable", @"[@:$]\w+", Kind.Variable),
            R("Number", Number, Kind.Number),
            R("Keyword", Words("select from where and or not in is null like between exists join inner left right full outer cross on as group by order having limit offset top distinct union all intersect except insert into values update set delete create alter drop table view index procedure function trigger database schema primary key foreign references constraint default unique check case when then else end begin commit rollback transaction declare return returns with recursive over partition asc desc grant revoke truncate merge using if exec execute go cast convert coalesce nullif count sum avg min max true false"), Kind.Keyword, matchCase: false),
            R("Type", Words("int integer bigint smallint tinyint bit decimal numeric float real double money char varchar nchar nvarchar text ntext date time datetime datetime2 timestamp boolean uuid uniqueidentifier json jsonb xml blob serial"), Kind.Type, matchCase: false),
            R("Function", FunctionCall, Kind.Function)),

        S("JSON", ".json .jsonc .json5 .jsonl .ndjson .geojson .har .webmanifest .babelrc .eslintrc .prettierrc",
            R("Comment", LineCommentSlash, Kind.Comment),
            R("Block comment", BlockComment, Kind.Comment),
            R("Key", @"""(?:[^""\\]|\\.)*""(?=\s*:)", Kind.Key),
            R("String", DoubleQuoted, Kind.String),
            R("Number", @"-?\b\d+(?:\.\d+)?(?:[eE][+-]?\d+)?\b", Kind.Number),
            R("Constant", Words("true false null"), Kind.Keyword)),

        S("XML", ".xml .xaml .axaml .csproj .vbproj .fsproj .vcxproj .props .targets .config .nuspec .resx .xsd .xsl .xslt .svg .plist .wsdl .manifest .xlf .xliff .rss .atom .kml .gpx .pom .dtd .xul .storyboard .xib",
            R("Comment", @"<!--.*?(?:-->|$)", Kind.Comment),
            R("Comment end", @"^[^<]*?-->", Kind.Comment),
            R("CDATA", @"<!\[CDATA\[.*?(?:\]\]>|$)", Kind.String),
            R("Declaration", @"<[?!][^>]*>?", Kind.Preprocessor),
            R("Tag", @"</?[\w:.-]+|/?>", Kind.Tag),
            R("Attribute", @"\b[\w:.-]+(?=\s*=\s*[""'])", Kind.Attribute),
            R("Value", @"""[^""]*""|'[^']*'", Kind.String),
            R("Entity", @"&#?\w+;", Kind.Escape)),

        S("HTML", ".html .htm .xhtml .shtml .vue .svelte .cshtml .razor .aspx .ascx .jsp .hbs .handlebars .mustache .liquid .njk .twig",
            R("Comment", @"<!--.*?(?:-->|$)", Kind.Comment),
            R("Comment end", @"^[^<]*?-->", Kind.Comment),
            R("Doctype", @"<!\w[^>]*>?", Kind.Preprocessor),
            R("Template", @"\{\{.*?\}\}|\{%.*?%\}|@\(?[\w.]+", Kind.Variable),
            R("Tag", @"</?[\w:.-]+|/?>", Kind.Tag),
            R("Attribute", @"(?<=\s)[@:#]?[\w:.-]+(?=\s*=)", Kind.Attribute),
            R("Value", @"""[^""]*""|'[^']*'", Kind.String),
            R("Entity", @"&#?\w+;", Kind.Escape)),

        S("CSS", ".css .scss .sass .less .styl .pcss",
            R("Block comment", BlockComment, Kind.Comment),
            R("Doc comment line", DocCommentLine, Kind.Comment),
            R("Block comment end", BlockCommentEnd, Kind.Comment),
            R("Comment", @"(?<![:\w])//.*", Kind.Comment),
            R("String", DoubleQuoted, Kind.String),
            R("String", SingleQuoted, Kind.String),
            R("At-rule", @"@[\w-]+", Kind.Control),
            R("Variable", @"(?:--|\$|@)[\w-]+", Kind.Variable),
            R("Property", @"(?<=[{;\s]|^)[a-z-]+(?=\s*:(?!:)[^{]*(?:;|$))", Kind.Key),
            R("Color", @"#[0-9a-fA-F]{3,8}\b", Kind.Number),
            R("Number", @"-?\b\d+(?:\.\d+)?(?:px|em|rem|vh|vw|vmin|vmax|%|s|ms|deg|fr|pt|ch|ex)?\b", Kind.Number),
            R("Selector", @"[.#][\w-]+|::?[\w-]+", Kind.Tag),
            R("Important", "!important", Kind.Keyword),
            R("Function", @"\b[\w-]+(?=\()", Kind.Function)),

        S("YAML", ".yml .yaml .clang-format .clang-tidy",
            R("Comment", @"(?<!\S)#.*", Kind.Comment),
            R("Document marker", @"^(?:---|\.\.\.)\s*$", Kind.Preprocessor),
            R("Key", @"^\s*(?:-\s+)?[^\s#:'""-][^#:]*?(?=\s*:(?:\s|$))|^\s*(?:-\s+)?(?:""[^""]*""|'[^']*')(?=\s*:(?:\s|$))", Kind.Key),
            R("String", DoubleQuoted, Kind.String),
            R("String", @"'(?:[^']|'')*'?", Kind.String),
            R("Anchor", @"[&*][\w-]+|!![\w]+|!\w+", Kind.Type),
            R("Constant", Words("true false yes no on off null"), Kind.Keyword, matchCase: false),
            R("Number", @"(?<![\w.-])-?\d+(?:\.\d+)?(?![\w.-])", Kind.Number),
            R("Block scalar", @"[|>][+-]?\s*$", Kind.Punctuation)),

        S("TOML", ".toml Pipfile",
            R("Comment", LineCommentHash, Kind.Comment),
            R("Table", @"^\s*\[\[?[^\]]*\]\]?", Kind.Heading),
            R("Key", @"^\s*[\w.""'-]+(?=\s*=)", Kind.Key),
            R("Multi-line string", @"(?:""""""|''').*?(?:""""""|'''|$)", Kind.String),
            R("String", DoubleQuoted, Kind.String),
            R("String", @"'[^']*'?", Kind.String),
            R("Date", @"\b\d{4}-\d{2}-\d{2}(?:[T ]\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?(?:Z|[+-]\d{2}:\d{2})?)?\b", Kind.Number),
            R("Number", @"[+-]?\b(?:0x[0-9a-fA-F_]+|\d[\d_]*(?:\.\d+)?(?:[eE][+-]?\d+)?)\b|\b(?:inf|nan)\b", Kind.Number),
            R("Constant", Words("true false"), Kind.Keyword)),

        S("INI / Properties", ".ini .cfg .conf .properties .env .editorconfig .gitconfig .gitattributes .npmrc .inf .desktop .service .url .reg .prefs",
            R("Comment", @"^\s*[;#!].*", Kind.Comment),
            R("Section", @"^\s*\[[^\]]*\]?", Kind.Heading),
            R("Key", @"^\s*(?:export\s+)?[^=:\s;#\[][^=:]*?(?=\s*[=:])", Kind.Key),
            R("String", DoubleQuoted, Kind.String),
            R("Variable", @"\$\{[^}]*\}|\$\w+|%\w+%", Kind.Variable),
            R("Constant", Words("true false yes no on off"), Kind.Keyword, matchCase: false),
            R("Number", @"(?<=[=:]\s*)-?\d+(?:\.\d+)?\s*$", Kind.Number)),

        S("HCL / Terraform", ".tf .tfvars .hcl .nomad",
            R("Comment", @"(?://|#).*", Kind.Comment),
            R("Block comment", BlockComment, Kind.Comment),
            R("String", DoubleQuoted, Kind.String),
            R("Interpolation", @"\$\{[^}]*\}", Kind.Variable),
            R("Block", @"^\s*(?:resource|data|variable|output|locals|module|provider|terraform|backend|required_providers|dynamic|job|group|task)\b", Kind.Keyword),
            R("Key", @"\b[\w-]+(?=\s*=(?!=))", Kind.Key),
            R("Number", Number, Kind.Number),
            R("Constant", Words("true false null for in if"), Kind.Keyword),
            R("Function", FunctionCall, Kind.Function)),

        S("GraphQL", ".graphql .gql .graphqls",
            R("Comment", LineCommentHash, Kind.Comment),
            R("Block string", "\"\"\".*?(?:\"\"\"|$)", Kind.String),
            R("String", DoubleQuoted, Kind.String),
            R("Variable", @"\$\w+", Kind.Variable),
            R("Directive", @"@\w+", Kind.Function),
            R("Keyword", Words("query mutation subscription fragment on type input enum interface union scalar schema extend implements directive repeatable true false null"), Kind.Keyword),
            R("Type", PascalCaseType, Kind.Type),
            R("Number", Number, Kind.Number)),

        S("Markdown", ".md .markdown .mdown .mkd .mdx .rmd",
            R("Heading", @"^#{1,6}\s.*", Kind.Heading),
            R("Code fence", @"^\s*(?:```|~~~).*", Kind.Preprocessor),
            R("Inline code", @"`[^`]+`", Kind.String),
            R("Quote", @"^\s*>.*", Kind.Comment),
            R("List marker", @"^\s*(?:[-*+]|\d+[.)])(?=\s)", Kind.Punctuation),
            R("Rule", @"^\s*(?:[-*_]\s*){3,}$", Kind.Punctuation),
            R("Link", @"!?\[[^\]]*\]\([^)]*\)|<https?://[^>]+>", Kind.Key),
            R("Bold", @"\*\*[^*]+\*\*|__[^_]+__", Kind.Keyword),
            R("Emphasis", @"(?<![\w*])\*[^*\s][^*]*\*(?!\*)|(?<!\w)_[^_\s][^_]*_(?!\w)", Kind.Type),
            R("HTML", @"</?[a-zA-Z][^>]*>", Kind.Tag)),

        S("Dockerfile", "Dockerfile .dockerfile Containerfile",
            R("Comment", @"^\s*#.*", Kind.Comment),
            R("Instruction", @"^\s*(?:FROM|RUN|CMD|LABEL|MAINTAINER|EXPOSE|ENV|ADD|COPY|ENTRYPOINT|VOLUME|USER|WORKDIR|ARG|ONBUILD|STOPSIGNAL|HEALTHCHECK|SHELL)\b", Kind.Keyword, matchCase: false),
            R("Stage", @"\b(?:AS|as)\s+\w[\w.-]*", Kind.Type),
            R("String", DoubleQuoted, Kind.String),
            R("String", @"'[^']*'?", Kind.String),
            R("Variable", @"\$\{[^}]*\}|\$\w+", Kind.Variable),
            R("Flag", @"(?<=\s)--[\w-]+(?:=\S*)?", Kind.Attribute)),

        S("Makefile", "Makefile GNUmakefile .mk .mak .make",
            R("Comment", @"(?<!\\)#.*", Kind.Comment),
            R("Directive", @"^\s*-?(?:include|sinclude|ifeq|ifneq|ifdef|ifndef|else|endif|define|endef|export|unexport|override|vpath)\b", Kind.Control),
            R("Target", @"^[^\s:=#][^:=#]*?(?=\s*::?(?!=))", Kind.Function),
            R("Variable assignment", @"^\s*[\w.-]+(?=\s*(?:[:+?!]?=))", Kind.Key),
            R("Variable", @"\$[({][^)}]*[)}]|\$[@<^+?*%]|\$\w", Kind.Variable),
            R("String", DoubleQuoted, Kind.String),
            R("String", @"'[^']*'?", Kind.String)),

        S("CMake", ".cmake",
            R("Comment", LineCommentHash, Kind.Comment),
            R("String", DoubleQuoted, Kind.String),
            R("Variable", @"\$\{[^}]*\}|\$ENV\{[^}]*\}", Kind.Variable),
            R("Command", @"\b\w+(?=\s*\()", Kind.Function),
            R("Keyword", Words("PUBLIC PRIVATE INTERFACE REQUIRED COMPONENTS VERSION ON OFF TRUE FALSE AND OR NOT STREQUAL EQUAL MATCHES DEFINED"), Kind.Keyword)),

        S("Diff / Patch", ".diff .patch .rej",
            R("File header", @"^(?:diff |index |---|\+\+\+|new file|deleted file|similarity|rename |old mode|new mode).*", Kind.Heading),
            R("Hunk", "^@@.*?@@", Kind.Control),
            R("Added", @"^\+.*", Kind.Added),
            R("Removed", @"^-.*", Kind.Removed),
            R("Note", @"^\\.*", Kind.Comment)),

        S("CSV / TSV", ".csv .tsv .psv .tab",
            R("Quoted field", @"""(?:[^""]|"""")*""?", Kind.String),
            R("Separator", @"[,;\t|]", Kind.Punctuation)),

        S("Assembly", ".asm .s .nasm .masm .inc",
            R("Comment", @"[;#].*|//.*", Kind.Comment),
            R("String", DoubleQuoted, Kind.String),
            R("String", SingleQuoted, Kind.String),
            R("Label", @"^\s*[\w.$]+:", Kind.Function),
            R("Directive", @"(?<!\w)\.[a-zA-Z]\w*|\b(?:section|segment|global|extern|db|dw|dd|dq|resb|resw|resd|equ|times|org|bits)\b", Kind.Preprocessor, matchCase: false),
            R("Register", Words("rax rbx rcx rdx rsi rdi rbp rsp r8 r9 r10 r11 r12 r13 r14 r15 eax ebx ecx edx esi edi ebp esp ax bx cx dx si di bp sp al ah bl bh cl ch dl dh cs ds es fs gs ss rip eip"), Kind.Variable, matchCase: false),
            R("Number", @"\b(?:0x[0-9a-fA-F]+|[0-9a-fA-F]+h|\d+)\b|\$[0-9a-fA-F]+", Kind.Number, matchCase: false),
            R("Instruction", @"^\s*(?:[\w.$]+:\s*)?\b[a-zA-Z]{2,7}\b", Kind.Keyword)),

        S("Nginx", ".nginx",
            R("Comment", LineCommentHash, Kind.Comment),
            R("String", DoubleQuoted, Kind.String),
            R("String", @"'[^']*'?", Kind.String),
            R("Variable", @"\$\w+", Kind.Variable),
            R("Block", @"^\s*(?:http|server|location|upstream|events|stream|map|if|types|geo|limit_except)\b", Kind.Control),
            R("Directive", @"^\s*[a-z_]+(?=\s)", Kind.Key),
            R("Number", @"\b\d+[kKmMgGsShHdD]?\b", Kind.Number)),
    ];
}
