using System;
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
    public DateTime WorkDate { get; set; }
    public string WorkTimes { get; set; }
    public float WorkHours { get; set; }
    public float TotalHours { get; set; }

    public Work_Hours()
    {

    }

    static List<Work_Hours> Get(DateTime work_date)
    {
      string sql = @"
        SELECT 
          work_hours_id WorkHoursId, 
          employee_id EmployeeId, 
          work_date WorkDate, 
          work_times WorkTimes, 
          work_hours WorkHours, 
          total_hours TotalHours
        FROM Work_Hours
        WHERE work_date=CAST(@WorkDate AS DATE)";

      DynamicParameters dp = new DynamicParameters();
      dp.Add("@WorkDate", DateTime.Today);
      return Program.Get_Data<Work_Hours>(sql, dp, Program.CS_Type.Timestore);
    }

  }
}
