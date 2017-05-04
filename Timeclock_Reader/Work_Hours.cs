﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dapper;

namespace Timeclock_Reader
{
  class Work_Hours
  {
    public long WorkHoursId { get; set; }
    public int EmployeeId { get; set; }
    public string DepartmentId { get; set; }
    public DateTime WorkDate { get; set; }
    public DateTime PayPeriodEnding { get; set; }
    public string WorkTimes { get; set; }
    public float WorkHours { get; set; }
    public float TotalHours { get; set; }

    public Work_Hours()
    {

    }

    public Work_Hours(Timeclock_Data tcd, string DeptId)
    {
      EmployeeId = tcd.EmployeeId;
      DepartmentId = DeptId;
      WorkDate = tcd.RoundedPunchDate;
      PayPeriodEnding = tcd.PayPeriodEnding;
      WorkTimes = tcd.RoundedPunchTime_ToString;
      WorkHours = 0;
      TotalHours = 0;
    }

    public void AddPunch(Timeclock_Data tcd, string[] tl)
    {
      // this function will take a new timepunch and add it to 
      // the existing work_hours row that we already have for that user.
      if(WorkTimes.Length > 0)
      {
        WorkTimes += " - " + tcd.RoundedPunchTime_ToString;
      }
      else
      {
        WorkTimes = tcd.RoundedPunchTime_ToString;
      }

      string[] times = WorkTimes.Split(new[] { " - " }, StringSplitOptions.RemoveEmptyEntries);

      float initialWH = WorkHours;
      float initialTH = TotalHours;

      if (times.Length % 2 == 1)
      {
        // We added an odd punch, we shouldn't update any time calculations

      }
      else
      {
        // we need to get the hours between each of our entries
        // sum it all up and update Workhours and TotalHours
        // we've already updated WorkTimes
        float WH = 0;
        for(int i = 0; i < times.Length; i += 2)
        {
          int start = Array.IndexOf(tl, times[i]);
          int end = Array.IndexOf(tl, times[i+1]);
          WH += ((float)(end - start) / 4);
        }
        if(WH != initialWH)
        {
          WorkHours = WH;
          TotalHours = TotalHours - initialWH + WorkHours;
        }
      }
    }

    public static List<Work_Hours> Get(DateTime work_date)
    {
      // this query returns everything from the earliest workday we have a timeclock stamp for.
      string sql = @"
        SELECT 
          work_hours_id WorkHoursId, 
          employee_id EmployeeId, 
          dept_id DepartmentId,
          work_date WorkDate, 
          work_times WorkTimes, 
          work_hours WorkHours, 
          total_hours TotalHours
        FROM Work_Hours
        WHERE work_date >= CAST(@WorkDate AS DATE)";

      DynamicParameters dp = new DynamicParameters();
      dp.Add("@WorkDate", DateTime.Today);
      return Program.Get_Data<Work_Hours>(sql, dp, Program.CS_Type.Timestore);
    }

    public bool Update()
    {
      string sql = @"
        UPDATE Work_Hours
          SET work_hours = @WorkHours, 
            work_times = @WorkTimes, 
            total_hours = @TotalHours
          WHERE work_hours_id = @WorkHoursId";
      try
      {
        long l = Program.Exec_Query(sql, this, Program.CS_Type.Timestore);
        if (l > 1)
        {
          Program.Log("Duplicate entries found in the Work_Hours table in Timestore",
            EmployeeId.ToString(),
            WorkDate.ToShortDateString(),
            "");
        }
        return (l > 0);
      }
      catch (Exception ex)
      {
        Program.Log(ex);
        return false;
      }
    }
    
    public bool Insert()
    {
      string sql = @"
        INSERT INTO Work_Hours 
          (employee_id, dept_id, pay_period_ending, 
          work_date, work_times, break_credit, work_hours, holiday, 
          leave_without_pay, total_hours, doubletime_hours, vehicle,
          comment, by_employeeid, by_username, by_machinename, 
          by_ip_address)
        VALUES 
          (@EmployeeId, @DepartmentId, @PayPeriodEnding, 
          @WorkDate, @WorkTimes, 0, @WorkHours, 0, 
          0, @TotalHours, 0, 0, 
          '', @EmployeeId, 'TCReader', 'TCReader', 
          'TCReader');";
      try
      {
        return Program.Save_Data<Work_Hours>(sql, this, Program.CS_Type.Timestore);
      }
      catch(Exception ex)
      {
        Program.Log(ex);
        return false;
      }
    }
    


  }
}
