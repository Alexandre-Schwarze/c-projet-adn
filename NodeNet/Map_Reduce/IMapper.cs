﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeNet.Map_Reduce
{
    public interface IMapper : ICloneable
    {
        Object Map(Object input,int nbMap);

        bool mapIsEnd();

        IMapper reset();

        new object Clone();
    }
}
