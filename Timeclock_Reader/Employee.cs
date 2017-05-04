using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Timeclock_Reader
{
  class Employee
  {
    public int EmployeeId { get; set; }
    public string DepartmentId { get; set; }

    public Employee()
    {

    }

    public static List<Employee> Get()
    {
      string sql = @"
        SELECT
          E.empl_no EmployeeId,
          E.home_orgn DepartmentId
        FROM employee E
        INNER JOIN person P ON E.empl_no = P.empl_no
        WHERE P.term_date IS NULL
        ORDER BY E.empl_no ASC";
      try
      {
        return Program.Get_Data<Employee>(sql, Program.CS_Type.Finplus);
      }
      catch(Exception ex)
      {
        Program.Log(ex);
        return null;
      }
    }
  }
}
