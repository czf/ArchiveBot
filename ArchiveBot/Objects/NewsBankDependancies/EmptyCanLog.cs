using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Czf.Domain.NewsBankWrapper.Interfaces;
using Microsoft.Azure.WebJobs.Host;

namespace ArchiveBot.Objects.NewsBankDependancies
{
    public class EmptyCanLog : ICanLog
    {

        TraceWriter writer;
        public void SetLog(TraceWriter log)
        {
            writer = log;
        }

        public void Error(string message)
        {
            writer.Error(message);
        }

        public void Info(string message)
        {
            writer.Info(message);
        }
    }
}
