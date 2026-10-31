using BoardGame.AppRuntime;
using BoardGame.Runtime;

namespace Example.RuntimePlugin;

public static class HostServiceConsumer
{
    public static ApprovedApplicationPlugin Create()=>new(_=>new("sample.consumer","1",1,["sample.consumer"],["sample.host"],scope=>
        scope.Provide("sample.consumer",scope.Service<object>("sample.host"))));
}
