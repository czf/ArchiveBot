using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Czf.Domain.NewsBankWrapper.Interfaces;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.KeyVault.Models;
//using Microsoft.Azure.Services.AppAuthentication;

namespace ArchiveBot.Core.Objects.NewsBankDependancies
{
    public class AzureKeyVaultEZProxySignInCredentialsProvider : IEZProxySignInCredentialsProvider
    {

        public string GetAccount()
        {
            throw new NotImplementedException();
        }

        public string GetPassword()
        {
            throw new NotImplementedException();
        }
    }
}
