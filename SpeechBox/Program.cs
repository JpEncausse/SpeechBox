using System;
using System.IO;
using System.Windows.Forms;
using System.Diagnostics;
using NDesk.Options;
using System.Collections.Generic;

namespace net.encausse.SpeechBox
{
	/// <summary>
	/// 
	/// </summary>
	static class Program
	{
		/// <summary>
		/// The main entry point for the application.
		/// </summary>
		[STAThread]
    static void Main (string[] args)
		{

      // ------------------------------------------
      //  OPTIONS
      // ------------------------------------------

      bool help = false;
      String directory = null;
      String language = "fr-FR";
      String url = null;
      String rss = null;

      var p = new OptionSet() {
        { "d|directory=", "the {DIRECTORY} of M4A.", v => directory = v },
        { "l|lang=", "the {LANGUAGE} Culture. Default is fr-FR", v => language = v },
        { "url=", "the {URL} to send text. (ie http://127.0.0.1/do?text=)", v => url = v },
        { "rss=", "the {path/to/feed.rss} of speech RSS feed.", v => rss = v },
        { "h|help",  "show this message and exit", v => help = v != null },
      };

      List<string> extra;
      try { extra = p.Parse(args);  }
      catch (OptionException e) {
        Console.Write("SpeechBox: ");
        Console.WriteLine(e.Message);
        Console.WriteLine("Try `SpeechBox --help' for more information.");
        Application.Exit();
      }

      if (help) { 
        ShowHelp(p); 
        Application.Exit(); 
      }

      if (!Directory.Exists(directory)) {
        Debug.WriteLine("Directory do not exists: " + directory);
        Application.Exit();
      }

      // ------------------------------------------
      //  STARTING
      // ------------------------------------------

			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);

			// Show the system tray icon.					
      using (ProcessIcon pi = new ProcessIcon(directory))
			{
				pi.Display();

        // Start Speech2Text
        Speech2Text s2t = new Speech2Text(directory, language, url, rss);

				// Make sure the application runs!
				Application.Run();
			}
		}

    static void ShowHelp (OptionSet p) {
      Console.WriteLine("Usage: SpeechBox [OPTIONS]+ message");
      Console.WriteLine();
      Console.WriteLine("Options:");
      p.WriteOptionDescriptions(Console.Out);
    }
	}
}