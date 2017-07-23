﻿using System;
using NodeNet.Data;
using NodeNet.Network.Nodes;
using NodeNet.Map_Reduce;

namespace NodeNet.Worker.Impl
{
    public class IdentitifierWorker : IWorker<Tuple<Boolean, String>, String>
    {
        public IMapper<Tuple<bool, string>, String> Mapper { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public IReducer<Tuple<bool, string>, String> Reducer { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public Action<DataInput> ProcessAction;

        public IdentitifierWorker(Action<DataInput> action)
        {
            ProcessAction = action;
        }

        public void CancelWork()
        {
            throw new NotImplementedException();
        }

        public String CastInputData(object data)
        {
            return (String)data;
        }

        public Tuple<bool, string> NodeWork(String input)
        {
            return new Tuple<bool, string>(true, input);
        }

        public void OrchWork(DataInput input)
        {
            ProcessAction(input);
        }

        public void ClientWork(DataInput input)
        {
            ProcessAction(input);
        }
    }
}