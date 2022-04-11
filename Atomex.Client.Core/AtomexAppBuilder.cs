using System;
using System.Collections.Generic;
using System.Text;

namespace Atomex
{
    public class AtomexAppBuilder
    {
        

        public virtual AtomexAppBuilder ConfigureServices()
        {
            // configure all services and dependencies
        }

        public AtomexApp Build()
        {
            return new AtomexApp()
            {
                // todo: fill services
            };
        }
    }
}