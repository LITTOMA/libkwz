using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using KwzStudio;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

internal static class LocalizationChecks
{
    public static void Run(MainWindow window,string root,string scratch,Action<bool,string> check,Action pump)
    {
        var english=L.GetCatalog("en");
        check(L.Languages.Count==20,"All 20 requested UI languages are registered");
        string[] Tokens(string value)=>Regex.Matches(value,@"\{\d+(?:,[^}:]+)?(?::[^}]+)?\}").Select(m=>m.Value).Order().ToArray();
        foreach(var language in L.Languages)
        {
            var catalog=L.GetCatalog(language.Code);
            check(catalog.Count==english.Count && catalog.Keys.Order().SequenceEqual(english.Keys.Order()),$"{language.Code}: complete embedded message catalog");
            foreach(var pair in catalog)
            {
                if(string.IsNullOrWhiteSpace(pair.Value) || !Tokens(pair.Value).SequenceEqual(Tokens(english[pair.Key])))
                    throw new InvalidDataException($"Invalid localized placeholders: {language.Code}: {pair.Key}");
                var format=CompositeFormat.Parse(pair.Value);
                if(format.MinimumArgumentCount>0)_=string.Format(System.Globalization.CultureInfo.GetCultureInfo(language.Code),format,Enumerable.Repeat<object>(1.25,format.MinimumArgumentCount).ToArray());
            }
            check(true,$"{language.Code}: translated format strings and placeholders are valid");
        }
        check(L.Resolve("zh-HK")=="zh-Hant" && L.Resolve("zh-SG")=="zh-Hans" && L.Resolve("zh-Hans-TW")=="zh-Hans" && L.Resolve("pt-PT")=="pt-BR" && L.Resolve("uk-UA")=="uk" && L.Resolve("en-GB")=="en" && L.Resolve("fi-FI")=="en","Regional language matching and English fallback");

        var doc=window.Document;
        window.Width=1440;window.Height=960;doc.New(3);
        doc.AddObject(new CanvasObject{Kind=ObjectKind.Text,Text="Keep this text 原文",X=20,Y=30,FontSize=20});
        doc.Edit("Change author",n=>n.Header.CurrentAuthorName="Author");
        string State()=>Convert.ToHexString(SHA256.HashData(doc.Note.ToArray()))+JsonSerializer.Serialize(doc.Objects);
        string before=State();var selection=doc.SelectedId;int revision=doc.Revision,frame=doc.Frame;
        string[] history=doc.History;bool dirty=doc.IsDirty;
        var author=window.GetVisualDescendants().OfType<TextBox>().Single(t=>t.Name=="CurrentAuthor");
        author.Text="Draft name";
        var statuses=new List<object>();
        foreach(var language in L.Languages)
        {
            L.SetLanguage(language.Code,persist:false);pump();
            check(State()==before && doc.SelectedId==selection && doc.Revision==revision && doc.Frame==frame && doc.IsDirty==dirty && doc.CanUndo && author.Text=="Draft name",$"{language.Code}: switching preserves unsaved objects, pixels, selection, author draft and undo state");
            var select=window.GetVisualDescendants().OfType<ToggleButton>().Single(c=>c.Name=="ToolSelect");
            check((string?)select.Content==L.T("Select V") && doc.History[0]==L.T("Change author"),$"{language.Code}: controls and existing history switch immediately");
            check(window.FlowDirection==(language.Code=="ar"?FlowDirection.RightToLeft:FlowDirection.LeftToRight) && window.Canvas.FlowDirection==FlowDirection.LeftToRight && window.Timeline.FlowDirection==FlowDirection.LeftToRight,$"{language.Code}: UI direction leaves artwork and timeline coordinates unchanged");
            check(window.Canvas.Bounds.Width>=320 && window.Canvas.Bounds.Height>=180,$"{language.Code}: translated layout keeps a usable canvas");
            foreach(var button in window.GetVisualDescendants().OfType<ToggleButton>().Where(c=>c.Name?.StartsWith("Tool")==true))
            {
                var origin=button.TranslatePoint(default,window)!.Value;
                if(origin.X<0 || origin.X+button.Bounds.Width>window.Width+1)throw new InvalidDataException($"Clipped tool: {language.Code}/{button.Name}");
            }
            using(var capture=window.CaptureRenderedFrame())
            {
                check(capture is not null && capture.PixelSize.Width==1440,$"{language.Code}: real Skia UI render succeeds");
                if(language.Code is "hi" or "th" or "de")capture!.Save(Path.Combine(scratch,$"review-{language.Code}.png"));
            }
            statuses.Add(new{language=language.Code,messages=english.Count,liveSwitch=true,projectUnchanged=true,rendered=true});
        }
        L.SetLanguage("en",persist:false);pump();
        check(doc.History.SequenceEqual(history),"Language changes do not rewrite history identifiers");
        doc.Undo();check(doc.Note.Header.CurrentAuthorName!="Author","Undo still works after all language switches");
        doc.Redo();check(State()==before,"Redo restores original objects and data after language switches");

        string settings=Path.Combine(scratch,"language-settings.json");
        L.Initialize(settings,"de-DE");
        var menu=window.GetVisualDescendants().OfType<Menu>().Single();
        var languageMenu=((IEnumerable<MenuItem>)menu.ItemsSource!).Single(m=>m.Name=="LanguageMenu");
        var japanese=((IEnumerable<Control>)languageMenu.ItemsSource!).OfType<MenuItem>().Single(m=>m.Name=="Language_ja");
        japanese.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));pump();
        check(L.Current=="ja" && File.Exists(settings),"Selecting a native language menu entry applies and saves it");
        L.Initialize(settings,"en-US");
        check(L.Current=="ja" && L.Preference=="ja","Language preference survives reinitialization");
        L.SetLanguage("system");L.Initialize(settings,"de-DE");
        check(L.Current=="de" && L.Preference=="system","System-language preference follows the next startup culture");
        File.WriteAllText(settings,"{broken");L.Initialize(settings,"zh-TW");
        check(L.Current=="zh-Hant","Corrupt preferences fall back safely to the system language");
        bool rejected=false;try{L.SetLanguage("unknown",persist:false);}catch(ArgumentException){rejected=true;}
        check(rejected && L.Current=="zh-Hant","Invalid explicit languages are rejected without changing the UI");
        string blocked=Path.Combine(scratch,"blocked-settings");Directory.CreateDirectory(blocked);L.Initialize(blocked,"en");
        bool saveFailed=false;try{L.SetLanguage("fr");}catch(IOException){saveFailed=true;}
        check(saveFailed && L.Current=="en" && !Directory.EnumerateFiles(scratch,"*.tmp").Any(),"Preference write failures preserve the current language and clean temporary files");
        L.Initialize(settings,"en");

        doc.Open(Path.Combine(root,"test/LibKwz.Test/sound.kwz"));doc.Seek(12);
        foreach(string code in new[]{"ja","ar"})
        {
            L.SetLanguage(code,persist:false);pump();
            using var capture=window.CaptureRenderedFrame();capture!.Save(Path.Combine(root,$"editor/preview-{code}.png"));
        }
        L.SetLanguage("en",persist:false);pump();
        File.WriteAllText(Path.Combine(root,"editor/localization-report.json"),JsonSerializer.Serialize(new{languages=20,messagesPerLanguage=english.Count,renderer="Avalonia.Headless + Skia",checks=statuses,preferencesVerified=true,undoRedoVerified=true,rtlArtworkUnchanged=true},new JsonSerializerOptions{WriteIndented=true}));
    }
}
