using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EsapiPlanCheck.Models
{
    public enum CheckStatus
    {
        Pass,
        Warning,
        Fail,
        NotApplicable
    }

    public class CheckResult
    {
        public string Title { get; set; }
        public string Category { get; set; }
        public CheckStatus Status { get; set; }
        public string Message { get; set; }
        public string Description { get; set; }
        public string Details { get; set; }
        public string Comments { get; set; }

        public CheckResult(string title, string category, CheckStatus status, string message, string description, string details = "")
        {
            Title = title;
            Category = category;
            Status = status;
            Message = message;
            Description = description;
            Details = details;
            Comments = "";
        }
    }
}
