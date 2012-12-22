using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Speech;
using System.Speech.Recognition;
using System.Globalization;
using System.Threading;
using System.Speech.AudioFormat;
using System.Net;
using System.Collections.Generic;
using System.IO.Compression;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using NAudio.Wave;
using CUETools.Codecs;
using CUETools.Codecs.FLAKE;


namespace net.encausse.SpeechBox {
  public class Speech2Text {

    protected String directory = null;
    protected CultureInfo culture = null;

    public Speech2Text (String directory, String language) { 
      this.directory = directory;
      if (!Directory.Exists(directory)) {
        throw new Exception("Directory do not exists: " + directory);
      }
      this.culture = new System.Globalization.CultureInfo(language);
      WatchFolder(directory);
      ProcessDirectory(directory);
    }

    // ------------------------------------------
    //   DIRECTORY WATCHER
    // ------------------------------------------

    public void WatchFolder (String directory) {

      FileSystemWatcher watcher = new FileSystemWatcher();
      watcher.Path = directory;
      watcher.Filter = "*.m4a";
      watcher.IncludeSubdirectories = false;
      watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size;
      watcher.Changed += new FileSystemEventHandler(watcher_Changed);
      watcher.EnableRaisingEvents = true; // Start
    }
    protected void watcher_Changed (object sender, FileSystemEventArgs e) {
      ProcessDirectory(directory);
    }

    // ------------------------------------------
    //   M4A to TXT
    // ------------------------------------------

    public void ProcessDirectory(String directory){
      DirectoryInfo dir = new DirectoryInfo(directory);
      foreach (FileInfo f in dir.GetFiles("*.m4a")) {
        if (File.Exists(f.FullName + ".txt")) { continue;  }
        String speech = ProcessFile(f.FullName);
        Debug.WriteLine("Speech: " + speech);
        System.IO.File.WriteAllText(f.FullName + ".txt", speech, Encoding.UTF8);
      }
    }

    public String ProcessFile (String file) {

      using (var reader = new MediaFoundationReader(file))
      using (var wav16 = new WaveFormatConversionStream(new WaveFormat(16000, 16, 1), reader)) 
      using (var stream = new MemoryStream() ){

        wav16.CopyTo(stream);  
        stream.Position = 0;

        var tmp = wav16;  
        tmp.Position = 0;

        /*
        Debug.WriteLine("File: " + file);
        Debug.WriteLine("BitsPerSample: " + tmp.WaveFormat.BitsPerSample +
                        " Channels: "     + tmp.WaveFormat.Channels +
                        " SampleRate: "   + tmp.WaveFormat.SampleRate +
                        " Encoding: "     + tmp.WaveFormat.Encoding);
        */

        // -------------
        //  MICROSOFT
        // -------------

        /*
        var wf = tmp.WaveFormat;
        SpeechRecognitionEngine sre = GetDictationEngine();
        sre.SetInputToAudioStream(stream, 
          new SpeechAudioFormatInfo(EncodingFormat.Pcm, wf.SampleRate, wf.BitsPerSample,  wf.Channels, wf.AverageBytesPerSecond,  wf.BlockAlign, null));
        RecognitionResult rr = sre.Recognize();
        Debug.WriteLine("Microsoft: " + rr.Text); 
        */

        // -------------
        //  GOOGLE
        // -------------

        var wavreader = new WAVReader(null, stream, new AudioPCMConfig(tmp.WaveFormat.BitsPerSample, tmp.WaveFormat.Channels, tmp.WaveFormat.SampleRate));
        wavreader.Position = 0;
        var response = Recognize("https://www.google.com/speech-api/v1/recognize?xjerr=1&client=chromium&maxresults=2", culture, wavreader);
        return response;
      }
    }

    // ------------------------------------------
    //   DICTATION ENGINE
    // ------------------------------------------

    protected SpeechRecognitionEngine dictanizer = null;
    public SpeechRecognitionEngine GetDictationEngine () {
      if (dictanizer != null) {
        return dictanizer;
      }

      dictanizer = new SpeechRecognitionEngine(culture);
      /*
      dictanizer.MaxAlternates = 2;
      dictanizer.InitialSilenceTimeout = TimeSpan.FromSeconds(3);
      dictanizer.BabbleTimeout = TimeSpan.FromSeconds(10);
      dictanizer.EndSilenceTimeout = TimeSpan.FromSeconds(2);
      dictanizer.EndSilenceTimeoutAmbiguous = TimeSpan.FromSeconds(2);
      */

      // Load a Dictation grammar
      DictationGrammar d = new DictationGrammar("grammar:dictation");
      d.Name = "dictation";
      dictanizer.LoadGrammar(d);

      return dictanizer;
    }

    // ------------------------------------------
    //   GOOGLE ENGINE
    // ------------------------------------------

    private void ConfigureRequest (HttpWebRequest request) {
      request.KeepAlive = true;
      request.SendChunked = true;
      request.ContentType = "audio/x-flac; rate=16000";
      request.UserAgent = "Mozilla/5.0 (Windows NT 6.2; WOW64) AppleWebKit/535.2 (KHTML, like Gecko) Chrome/15.0.874.121 Safari/535.2";
      request.Headers.Set(HttpRequestHeader.AcceptEncoding, "gzip,deflate,sdch");
      request.Headers.Set(HttpRequestHeader.AcceptLanguage, "en-GB,en-US;q=0.8,en;q=0.6");
      request.Headers.Set(HttpRequestHeader.AcceptCharset, "ISO-8859-1,utf-8;q=0.7,*;q=0.3");
      request.Method = "POST";
    }

    public String Recognize (String url, CultureInfo culture, WAVReader wavReader) {

      // Build request
      var request = (HttpWebRequest)WebRequest.Create(url + "&lang=" + culture.Name + "&maxresults=6&pfilter=2");
      ConfigureRequest(request);

      // Convert to FLAC
      var requestStream = request.GetRequestStream();
      ConvertToFlac(wavReader, requestStream);

      // Parse Response
      var response = request.GetResponse();
      var match = "";
      using (var responseStream = response.GetResponseStream())
      using (var zippedStream = new GZipStream(responseStream, CompressionMode.Decompress)) {
        var json = Deserialise<RecognizedText>(zippedStream);
        match = json.Hypotheses[0].Utterance;
      }
      response.Close();
      return match;
    }

    // ------------------------------------------
    //   SERIALIZATION
    // ------------------------------------------
    // {"status":0,"id":"2526dd613c874c321bf9abecd5331ed1-1","hypotheses":[{"utterance":"this is a test this is a test","confidence":0.92601025},{"utterance":"this is a test this is the test"},{"utterance":"this is a test this is a text"},{"utterance":"this is a test this is a test txt"},{"utterance":"this is the test this is a test"},{"utterance":"this is a test this is a test hey"}]}

    private T Deserialise<T> (Stream stream) {
      T obj = Activator.CreateInstance<T>();
      DataContractJsonSerializer serializer = new DataContractJsonSerializer(obj.GetType());
      obj = (T)serializer.ReadObject(stream);
      return obj;
    }

    [DataContract]
    public class RecognizedText {
      [DataMember(Name = "hypotheses")]
      public Hypothesis[] Hypotheses { get; set; }
      [DataMember(Name = "status")]
      public int Status { get; set; }
      [DataMember(Name = "id")]
      public string Id { get; set; }
    }

    [DataContract]
    public class Hypothesis {
      [DataMember(Name = "utterance")]
      public string Utterance { get; set; }
      [DataMember(Name = "confidence")]
      public double Confidence { get; set; }
    }

    // ------------------------------------------
    //   FLAC CONVERTER
    // ------------------------------------------

    private void ConvertToFlac (WAVReader audioSource, Stream destinationStream) {
      try {
        if (audioSource.PCM.SampleRate != 16000) {
          throw new InvalidOperationException("Incorrect frequency - WAV file must be at 16 KHz.");
        }
        var buff = new AudioBuffer(audioSource, 0x10000);
        var flakeWriter = new FlakeWriter(null, destinationStream, audioSource.PCM);
        flakeWriter.CompressionLevel = 8;
        while (audioSource.Read(buff, -1) != 0) {
          flakeWriter.Write(buff);
        }
        flakeWriter.Close();
      }
      finally {
        audioSource.Close();
      }
    }

  }
}
