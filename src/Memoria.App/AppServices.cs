using System;
using Microsoft.Extensions.DependencyInjection;

namespace Memoria.App;

/// App 전역 서비스 로케이터(계약 §9.2). 컴포지션 루트(App.xaml.cs)가 Initialize 하고,
/// View code-behind/부트스트랩에서만 Resolve&lt;T&gt;()로 사용한다(ViewModel은 생성자 주입 유지).
public static class AppServices
{
    private static IServiceProvider? _provider;

    public static IServiceProvider Provider =>
        _provider ?? throw new InvalidOperationException("AppServices.Initialize가 먼저 호출되어야 합니다.");

    public static T Resolve<T>() where T : notnull => Provider.GetRequiredService<T>();

    internal static void Initialize(IServiceProvider provider) => _provider = provider;
}
