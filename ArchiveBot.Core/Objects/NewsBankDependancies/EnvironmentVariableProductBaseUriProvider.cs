using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Czf.Domain.NewsBankWrapper.Interfaces;

namespace ArchiveBot.Core.Objects.NewsBankDependancies
{
    public class EnvironmentVariableProductBaseUriProvider : IProductBaseUriProvider
    {
        public Uri GetProductBaseUri() => new Uri(Environment.GetEnvironmentVariable("NewsbankProductBaseUri"));
    }
}
