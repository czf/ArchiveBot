using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Czf.Domain.NewsBankWrapper.Interfaces;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;

namespace ArchiveBot.Core.Objects.NewsBankDependancies
{
    public class BasicCanLog : ICanLog
    {

        ILogger writer;
        public BasicCanLog(ILogger log)
        {
            writer = log;
        }

        public void Error(string message)
        {
            writer.LogError(message);
        }

        public void Info(string message)
        {
            writer.LogInformation(message);
        }
    }
}
