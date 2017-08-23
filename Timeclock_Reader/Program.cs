using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Data;
using System.Data.SqlClient;
using Dapper;
using System.Configuration;

namespace Timeclock_Reader
{
  class Program
  {
    public const int appId = 20025;
    public const string Source_QQest = "Q";
    public const string Source_File = "F";
    public static string Log_CS = "";
    public static string Timestore_QA_CS = "";
    public static string Timestore_CS = "";
    public static string Qqest_CS = "";
    public static string Finplus_CS = "";
    const string File_Path = @"\\claybccpubtime\c$\TA100PRO\CLK\RFILES"; // location of the files we're going to parse.
    const string Old_File_Path = @"\\claybccpubtime\c$\TA100PRO\CLK\RFILES\Old";


    public enum CS_Type
    {
      Timestore,
      Qqest,
      Finplus,
      Log
    }
    //claybccpubtime
    // \\claybccpubtime\c$\TA100PRO\CLK\RFILES

    // the purpose of this application is to pull data in from one of two sources
    // Source A) 
    // Is the QQest database found on the MSCSQL server.  
    // It will only be used for testing, barring the unforseen.
    // Some changes may need to be made to accomodate the difference in 
    // QQest and Source B. 
    //
    // Source B) 
    // A file created each day by the time clock software
    // This software will need to be able to read (not write!)
    // from each file, and pick up where it left off, as each new
    // punch is appended to the file.
    // This is expected to be our long term solution.

    static void Main(string[] args)
    {
      //BuildPPDList();
      try
      {
        Log_CS = ConfigurationManager.ConnectionStrings["Logs"].ConnectionString;
        Timestore_QA_CS = ConfigurationManager.ConnectionStrings["TimestoreQA"].ConnectionString;
        Timestore_CS = ConfigurationManager.ConnectionStrings["TimestoreProd"].ConnectionString;
        Qqest_CS = ConfigurationManager.ConnectionStrings["Qqest"].ConnectionString;
        Finplus_CS = ConfigurationManager.ConnectionStrings["Finplus"].ConnectionString;
        HandleFiles();
        HandleQqest();
      }
      catch(Exception ex)
      {
        new ErrorLog(ex);
        Console.WriteLine(ex.ToString());
        Console.WriteLine(ex.StackTrace.ToString());
        Console.WriteLine(ex.Source.ToString());
      }
    }

    static void BuildPPDList()
    {
      var sb = new StringBuilder();
      int i = 0;
      DateTime Start = DateTime.Parse("10/10/2016");
      DateTime End = DateTime.Parse("1/15/2017");
      while (Start.AddDays(i) < End)
      {
        var d = Start.AddDays(i);
        sb.Append(d.ToShortDateString());
        sb.Append('\t').AppendLine(GetPayPeriodStart(d).AddDays(13).ToShortDateString());
        i++;
      }
      string s = sb.ToString();

    }

    static void HandleData(List<Timeclock_Data> current, string source)
    {
      if (current.Count() == 0) return; // if we don't find any entries in our files, we should exit.
      DateTime earliest = (from t in current
                           orderby t.RawPunchDate ascending
                           select t.RawPunchDate).First();
      // load data from database to compare
      var existing = Timeclock_Data.GetSavedTimeClockData(earliest, source);
      var timelist = Get_Timelist();
      var employees = Employee.Get();
      // remove duplicates
      current = (from t in current
                 where !t.Exists(existing)
                 select t).ToList();

      if (current.Count() == 0) return; // if we don't find any entries in our files, we should exit.
      // At this stage, tcd should contain any data that doesn't exist
      // in the Timeclock_Data in Timestore.
      // We'll save them to that table to start, because regardless of the age,
      // we should be saving punches if they don't exist yet.
      foreach (Timeclock_Data t in current)
      {
        t.SavePunchAndNote();
      }

      // exclude stuff prior this this pay period, 
      // we wouldn't use this data to update Timestore.
      current = (from t in current
                 orderby t.RawPunchDate descending
                 where !t.IsPastCutoff()
                 select t).ToList();

      if (current.Count() == 0) return;

      // update our earliest date just in case it's changed
      earliest = (from t in current
                  orderby t.RawPunchDate ascending
                  select t.RawPunchDate).First();

      var wdl = Work_Hours.Get(earliest); // this is what is currently in Timestore

      // save the new entries to Timestore
      foreach(Timeclock_Data t in current)
      {
        var check = (from w in wdl
                     where w.EmployeeId == t.EmployeeId && 
                     w.WorkDate.Date == t.RoundedPunchDate.Date
                     select w).ToList();
        if(check.Count() > 0)
        {
          // we already have an entry for this person in Timestore, 
          // we should update it rather than creating a new one.
          var currentWork = check.First();
          currentWork.AddPunch(t, timelist); // add this punch to this day
          currentWork.Update(); // update the database.
        }
        else
        {
          // let's add our data to Timestore.
          // First, we'll need to get the employee's department id
          var depts = (from e in employees
                       where e.EmployeeId == t.EmployeeId
                       select e.DepartmentId).ToList();
          string Dept = "";
          if (depts.Count > 0) Dept = depts.First();

          var w = new Work_Hours(t, Dept);
          w.Insert(); // this will create a row in Timestore.
          wdl.Add(w); // let's add it to our existing rows.
        }
      }
    }

    static void HandleQqest()
    {

      DateTime start = DateTime.Parse("5/3/2017"); // default value of the date.
      var tcdl = Timeclock_Data.GetLastSavedTimeClockData(Source_QQest);
      // we're going to go all the way back to the pay period start if this is found
      // because data in qqest can be changed whenever.
      if (tcdl.Count > 0) start = tcdl.First().PayPeriodEnding.AddDays(-13).Date;  // use the pay period starting date
      HandleData(Timeclock_Data.GetQqestData(start), Source_QQest);
    }

    static void HandleFiles()
    {
      // load data from files
      var files = GetFiles(); // get the filenames

      HandleData(ParseFiles(files), Source_File);

      // move old files so that we don't process them again needlessly.
      MoveOldFiles(files);
    }

    static List<string> GetFiles()
    {
      List<string> exportFiles = new List<string>();
      exportFiles.AddRange(Directory.GetFiles(File_Path, "*.R"));
      return exportFiles;
    }

    static void MoveOldFiles(List<string> files)
    {
      // the only file we're not going to move is today's file
      // so to figure out which file is today's, we'll generate
      // the filename for today's file and rename everything that's not that.
      string TodaysFile = DateTime.Today.ToString("MMddyyyy") + ".R";
      foreach (string f in files)
      {
        if (Path.GetFileName(f) != TodaysFile)
        {
          // let's rename this sucker.
          string NewFile = f.Replace(File_Path, Old_File_Path);
          try
          {
            File.Move(f, NewFile);
          }
          catch (Exception ex)
          {
            Program.Log(ex);
          }
        }
      }
    }

    static List<Timeclock_Data> ParseFiles(List<string> files)
    {
      List<Timeclock_Data> tcd = new List<Timeclock_Data>();
      foreach (string f in files)
      {
        string[] currentFile = File.ReadAllLines(f);
        foreach (string cf in currentFile)
        {
          tcd.Add(new Timeclock_Data(cf));
        }
      }
      return tcd;

    }

    static bool UseProduction()
    {
      switch (Environment.MachineName.ToUpper())
      {
        case "MISML01":
          return false;
        case "PRODSERVER":
          return true;
        default:
          return false;
      }
    }

    string Get_Source()
    {
      if (UseProduction())
      {
        return Source_File;
      }
      else
      {
        return Source_QQest;
      }
    }

    public static List<T> Get_Data<T>(string query, DynamicParameters dbA, CS_Type cs)
    {
      try
      {
        using (IDbConnection db = new SqlConnection(Get_ConnStr(cs)))
        {
          return (List<T>)db.Query<T>(query, dbA);
        }
      }
      catch (Exception ex)
      {
        Log(ex, query);
        return null;
      }
    }

    public static List<T> Get_Data<T>(string query, CS_Type cs)
    {
      try
      {
        using (IDbConnection db = new SqlConnection(Get_ConnStr(cs)))
        {
          return (List<T>)db.Query<T>(query);
        }
      }
      catch (Exception ex)
      {
        Log(ex, query);
        return null;
      }
    }

    public static int Save_Data<T>(string insertQuery, T item, CS_Type cs)
    {
      try
      {
        using (IDbConnection db = new SqlConnection(Get_ConnStr(cs)))
        {
          return db.Execute(insertQuery, item);
        }
      }
      catch (Exception ex)
      {
        Log(ex, insertQuery);
        return -1;
      }
    }

    public static int Save_Data(string insertQuery, DynamicParameters dbA, CS_Type cs)
    {
      try
      {
        using (IDbConnection db = new SqlConnection(Get_ConnStr(cs)))
        {
          return db.Execute(insertQuery, dbA);
        }
      }
      catch (Exception ex)
      {
        Log(ex, insertQuery);
        return -1;
      }
    }

    public static long Exec_Query(string query, DynamicParameters dbA, CS_Type cs)
    {
      try
      {
        using (IDbConnection db = new SqlConnection(Get_ConnStr(cs)))
        {
          return db.Execute(query, dbA);
        }
      }
      catch (Exception ex)
      {
        Log(ex, query);
        return -1;
      }
    }

    public static long Exec_Query<T>(string query, T item, CS_Type cs)
    {
      try
      {
        using (IDbConnection db = new SqlConnection(Get_ConnStr(cs)))
        {
          return db.Execute(query, item);
        }
      }
      catch (Exception ex)
      {
        Log(ex, query);
        return -1;
      }
    }

    public static DateTime GetPayPeriodStart(DateTime start)
    {
      return start.AddDays(-(start.Subtract(DateTime.Parse("9/25/2013")).TotalDays % 14));
    }

    static string[] Get_Timelist()
    {
      List<string> tl = new List<string>();
      DateTime d = DateTime.Today;
      for (int i = 0; i < 96; i++)
      {
        tl.Add(d.AddMinutes(15 * i).ToString("h:mm tt"));
      }
      tl.Add(d.AddSeconds(-1).ToString("h:mm:ss tt")); // add the 11:59:59 PM entry
      return tl.ToArray();
    }

    #region Log Code

    public static void Log(Exception ex, string Query = "")
    {
      SaveLog(new ErrorLog(ex, Query));
    }

    public static void Log(string Text, string Message,
      string Stacktrace, string Source, string Query = "")
    {
      ErrorLog el = new ErrorLog(Text, Message, Stacktrace, Source, Query);
      SaveLog(el);
    }

    private static void SaveLog(ErrorLog el)
    {
      string sql = @"
          INSERT INTO ErrorData 
          (applicationName, errorText, errorMessage, 
          errorStacktrace, errorSource, query)  
          VALUES (@applicationName, @errorText, @errorMessage,
            @errorStacktrace, @errorSource, @query);";

      using (IDbConnection db = new SqlConnection(Get_ConnStr(CS_Type.Log)))
      {
        db.Execute(sql, el);
      }
    }

    public static void SaveEmail(string to, string subject, string body)
    {
      string sql = @"
          INSERT INTO EmailList 
          (EmailTo, EmailSubject, EmailBody)  
          VALUES (@To, @Subject, @Body);";

      try
      {
        var dbArgs = new Dapper.DynamicParameters();
        dbArgs.Add("@To", to);
        dbArgs.Add("@Subject", subject);
        dbArgs.Add("@Body", body);


        using (IDbConnection db = new SqlConnection(Get_ConnStr(CS_Type.Log)))
        {
          db.Execute(sql, dbArgs);
        }
      }
      catch (Exception ex)
      {
        Log(ex, sql);
        Log("Payment Email not sent", subject, body, "");
      }
    }

    public static string Get_ConnStr(CS_Type cs)
    {
      switch (cs)
      {
        case CS_Type.Finplus:
          return Finplus_CS;

        case CS_Type.Log:
          return Log_CS;

        case CS_Type.Qqest:
          return Qqest_CS;

        case CS_Type.Timestore:
          if (UseProduction())
          {
            return Timestore_CS;
          }
          else
          {
            return Timestore_QA_CS;
          }
        default:
          return "";
      }
    }

    #endregion



  }
}
