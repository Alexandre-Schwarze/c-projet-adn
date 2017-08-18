﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeNet.Map_Reduce
{
    public interface IReducer : ICloneable
    {
        Object reduce(ConcurrentBag<Object> input);
        new object Clone();
    }
}
