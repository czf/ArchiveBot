using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArchiveBot.Core.Objects
{
    public interface IBotParameterCredentialsProvider
    {
        string BotName { get; }
        string BotPassword { get;  }
        string BotSecret { get;  }
        string BotClientId { get; }    
    }
}
