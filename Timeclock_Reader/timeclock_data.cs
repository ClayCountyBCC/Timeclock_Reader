using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dapper;

namespace Timeclock_Reader
{
  class Timeclock_Data
  {
    public int EmployeeId { get; set; }

    public string Source { get; set; }

    public DateTime RawPunchDate { get; set; } = DateTime.MinValue;

    public DateTime RoundedPunchDate {
      get
      {
        if (RawPunchDate == DateTime.MinValue) return DateTime.MinValue;
        int totalSeconds = RawPunchDate.Minute * 60 + RawPunchDate.Second;
        int secondsIntoRank = (totalSeconds % 450);
        int rank = (totalSeconds - secondsIntoRank)  / 450;
        switch (rank)
        {
          case 0: // 0 to 7.5 mins
            return RawPunchDate.AddSeconds(-totalSeconds); // resets to 0.

          case 1: // 7.5 to 15 mins
          case 2: // 15 to 22.5 mins
            return RawPunchDate.AddSeconds(900 - totalSeconds);

          case 3: // 22.5 to 30 mins
          case 4: // 30 to 37.5 mins
            return RawPunchDate.AddSeconds(1800 - totalSeconds);

          case 5: // 37.5 to 45 mins
          case 6: // 45 to 52.5 mins
            return RawPunchDate.AddSeconds(2700 - totalSeconds);

          case 7: // 52.5 to 60 mins
            if (RawPunchDate.Hour == 23)
            { // this is how we handle the edge case for punches that are just before midnight.
              return RawPunchDate.AddSeconds(3600 - totalSeconds - 1);
            }
            else
            {
              return RawPunchDate.AddSeconds(3600 - totalSeconds);
            }

          default:
            return RawPunchDate;
          
        }
      }
    }

    public DateTime PayPeriodEnding
    {
      get
      {
        return Program.GetPayPeriodStart(RawPunchDate).AddDays(13);
      }
    }

    public string Note
    {
      get
      {
        return $"Timeclock punched at {RawPunchDate.ToString("MM/dd/yyyy hh:mm:ss AMPM") }, rounded to {RoundedPunchDate.ToString("MM/dd/yyyy hh:mm:ss AMPM")}";
      }
    }

    public bool IsPastCutoff()
    {
      // cutoff is 10 AM of the first day on the new pay period.
      return DateTime.Now > Program.GetPayPeriodStart(RawPunchDate).AddDays(14).AddHours(10);
    }

    public Timeclock_Data()
    {

    }

    public Timeclock_Data(string line)
    {
      // This handles converting a line of text in the R file into a valid Timeclock_data
      // Info about this file:  
      // comma delimited
      // field 1: the account that created this file
      // field 2: the scanner location (ie: which site)
      // field 3: date 
      // field 4: time
      // field 5: employee id
      // all other fields are meaningless.
      try
      {
        string[] s = line.Split(new[] { "," }, StringSplitOptions.RemoveEmptyEntries);
        if (s.Length > 5)
        {
          RawPunchDate = DateTime.ParseExact(s[2] + " " + s[3], "yyyyMMdd HHmmss", null);
          Source = Program.Source_File;
          EmployeeId = int.Parse(s[4]);
        }
      }
      catch(Exception ex)
      {
        Program.Log(ex);
      }
    }

    public Timeclock_Data(string Source, int eId, DateTime raw)
    {
      this.Source = Source;
      EmployeeId = eId;
      RawPunchDate = raw;
    }

    static public List<Timeclock_Data> GetSavedTimeClockData(DateTime Start, string Source)
    {
      // this function is going to return all of the rows after a given start date
      // for a given source
      var dbArgs = new DynamicParameters();
      dbArgs.Add("@Source", Source);
      dbArgs.Add("@Start", Start);
      string sql = @"
        SELECT 
          employee_id EmployeeId,
          raw_punch_date RawPunchDate
          source SOURCE
        FROM Timeclock_Data
        WHERE source=@Source AND
          raw_punch_date >= @Start
        ORDER BY raw_punch_date ASC;";
      try
      {
        return Program.Get_Data<Timeclock_Data>(sql, dbArgs, Program.CS_Type.Timestore);
      }
      catch (Exception ex)
      {
        Program.Log(ex);
        return null;
      }
    }

    public bool Exists(List<Timeclock_Data> ltcd)
    {
      // this function is going to check and see if a record already exists in the list provided
      // that matches this employee number and raw punch date, and source.
      return (from l in ltcd
              where l.EmployeeId == EmployeeId &&
              l.RawPunchDate == RawPunchDate &&
              l.Source == Source
              select l).Count() > 0;
    }

    static public List<Timeclock_Data> GetQqestData(DateTime Start)
    {
      var dbArgs = new DynamicParameters();
      dbArgs.Add("@Start", Start);
      string sql = $@"
          SELECT 
            empMain.employeeid EmployeeId,
            'Q' AS Source,
            rawpunch_ts RawPunchDate
          FROM empMain 
          INNER JOIN timeWorkingPunch ON empMain.employee_id = timeWorkingPunch.employee_id 
          WHERE timeWorkingPunch.active_yn = 1 
            AND rawpunch_ts <> '1/1/2000'
            AND rawpunch_ts >= @Start";
      return Program.Get_Data<Timeclock_Data>(sql, dbArgs, Program.CS_Type.Qqest);
    }

    public bool SavePunch()
    {
      // this function will save this object's data to the timeclockdata table.
      string sql = @"
        INSERT INTO Timeclock_Data 
        (employee_id, raw_punch_date, rounded_punch_date, source)
        VALUES (@EmployeeId, @RawPunchDate, @RoundedPunchDate, @Source);";
      try
      {
        return Program.Save_Data<Timeclock_Data>(sql, this, Program.CS_Type.Timestore);
      }
      catch(Exception e)
      {
        Program.Log(e, sql);
        return false;
      }

    }

     public bool SaveTimeStoreNote()
    {
      // finally, we'll add a note for each timeclock entry.  
      // The format of the note will be Time punch <real time> rounded to <rounded time>
      string sql = @"
        INSERT INTO notes 
        (employee_id, pay_period_ending, note, note_added_by)
        VALUES (@EmployeeId, @PayPeriodEnding, @Note, 'Timeclock_Reader');";
      try
      {
        return Program.Save_Data<Timeclock_Data>(sql, this, Program.CS_Type.Timestore);
      }
      catch (Exception e)
      {
        Program.Log(e, sql);
        return false;
      }

    }

  }
}
