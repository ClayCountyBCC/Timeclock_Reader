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
    const string File_Path = @"\\claybccpubtime\c$\TA100PRO\CLK\RFILES"; // location of the files we're going to parse.


    public enum CS_Type
    {
      Timestore,
      Qqest,
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
      Log_CS = ConfigurationManager.ConnectionStrings["Logs"].ConnectionString;
      Timestore_QA_CS = ConfigurationManager.ConnectionStrings["TimestoreQA"].ConnectionString;
      Timestore_CS = ConfigurationManager.ConnectionStrings["TimestoreProd"].ConnectionString;
      Qqest_CS = ConfigurationManager.ConnectionStrings["Qqest"].ConnectionString;
      string TodaysFile = DateTime.Today.ToString("MMddyyyy") + ".R";
      HandleFiles();
      
      //var l = new List<Timeclock_Data>();
      //var d = DateTime.Parse("1/1/2017 6:45:00 AM");
      //for(int i = 0; i < 100; i++)
      //{
      //  l.Add(new Timeclock_Data("", 0, d.AddSeconds(i * 30)));
      //}
      //var t = l.Count();

    }

    static void HandleFiles()
    {
      // load data from files
      var files = GetFiles();
      var tcd = ParseFiles(files);
      // exclude stuff prior this this pay period
      tcd = (from t in tcd
             where !t.IsPastCutoff()
             select t).ToList();

      // load data from database to compare

      // remove duplicates
      // save new

      // move old files
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

    }


    static List<Timeclock_Data>ParseFiles(List<string> files)
    {
      List<Timeclock_Data> tcd = new List<Timeclock_Data>();
      foreach(string f in files)
      {
        string[] currentFile = File.ReadAllLines(f);
        foreach(string cf in currentFile)
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
      } else
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

    public static bool Save_Data<T>(string insertQuery, T item, CS_Type cs)
    {
      try
      {
        using (IDbConnection db = new SqlConnection(Get_ConnStr(cs)))
        {
          db.Execute(insertQuery, item);
          return true;
        }
      }
      catch (Exception ex)
      {
        Log(ex, insertQuery);
        return false;
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

    public static DateTime GetPayPeriodStart(DateTime start)
    {
      return start.AddDays(-(start.Subtract(DateTime.Parse("9/25/2013")).TotalDays % 14));
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
