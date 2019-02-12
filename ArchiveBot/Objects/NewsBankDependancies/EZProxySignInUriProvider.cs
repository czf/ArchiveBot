using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Czf.Domain.NewsBankWrapper.Interfaces;

namespace ArchiveBot.Objects.NewsBankDependancies
{
    public class EnvironmentVariableEZProxySignInUriProvider : IEZProxySignInUriProvider
    {
        public Uri GetSignInUri()
        {
            return new Uri(Environment.GetEnvironmentVariable("EzProxySignInUri"));
        }
    }
}
