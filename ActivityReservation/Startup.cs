﻿using System;
using System.IO;
using ActivityReservation.API;
using ActivityReservation.Business;
using ActivityReservation.Common;
using ActivityReservation.Database;
using ActivityReservation.Events;
using ActivityReservation.Extensions;
using ActivityReservation.Helpers;
using ActivityReservation.Models;
using ActivityReservation.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Swashbuckle.AspNetCore.Swagger;
using WeihanLi.Common;
using WeihanLi.Common.Event;
using WeihanLi.Common.Helpers;
using WeihanLi.Common.Http;
using WeihanLi.Common.Logging.Log4Net;
using WeihanLi.EntityFramework;
using WeihanLi.Web.Extensions;

namespace ActivityReservation
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration.ReplacePlaceholders();
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddHealthChecks();

            services.AddMvc()
                .AddJsonOptions(options =>
                {
                    options.SerializerSettings.ContractResolver = new DefaultContractResolver();
                    options.SerializerSettings.DateTimeZoneHandling = DateTimeZoneHandling.Utc; // 设置时区为 UTC
                    options.SerializerSettings.NullValueHandling = NullValueHandling.Ignore;
                    options.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
                })
                .SetCompatibilityVersion(CompatibilityVersion.Latest);

            services.AddDataProtection().SetApplicationName(ApplicationHelper.ApplicationName);

            //Cookie Authentication
            services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(options =>
                {
                    options.AccessDeniedPath = "/Admin/Account/Login";
                    options.LoginPath = "/Admin/Account/Login";
                    options.LogoutPath = "/Admin/Account/LogOut";

                    // Cookie settings
                    options.Cookie.HttpOnly = true;
                    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                });

            // addDbContext
            services.AddDbContext<ReservationDbContext>(option => option.UseSqlite(Configuration.GetConnectionString("Reservation")));

            services.AddHttpClient<TencentCaptchaHelper>(client => client.Timeout = TimeSpan.FromSeconds(3))
                .ConfigurePrimaryHttpMessageHandler(() => new NoProxyHttpClientHandler());
            services.AddTencentCaptchaHelper(options =>
            {
                options.AppId = Configuration["Tencent:Captcha:AppId"];
                options.AppSecret = Configuration["Tencent:Captcha:AppSecret"];
            });

            services.AddEFRepository();
            services.AddBLL();

            services.AddSingleton<OperLogHelper>();
            services.AddScoped<ReservationHelper>();

            // registerApplicationSettingService
            services.TryAddSingleton<IApplicationSettingService, ApplicationSettingInMemoryService>();
            // register access control service
            services.AddAccessControlHelper<Filters.AdminPermissionRequireStrategy, Filters.AdminOnlyControlAccessStrategy>();

            services.AddHttpClient<ChatBotHelper>(client =>
                {
                    client.Timeout = TimeSpan.FromSeconds(3);
                })
                .ConfigurePrimaryHttpMessageHandler(() => new NoProxyHttpClientHandler());
            services.TryAddSingleton<ChatBotHelper>();

            services.AddHttpClient<WechatAPI.Helper.WechatHelper>()
                .ConfigurePrimaryHttpMessageHandler(() => new NoProxyHttpClientHandler());
            services.TryAddSingleton<WechatAPI.Helper.WechatHelper>();

            services.TryAddSingleton<CaptchaVerifyHelper>();

            services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc(ApplicationHelper.ApplicationName, new Info { Title = "活动室预约系统 API", Version = "1.0" });

                options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, $"{typeof(Notice).Assembly.GetName().Name}.xml"));
                options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, $"{typeof(NoticeController).Assembly.GetName().Name}.xml"), true);
            });

            services.AddSingleton<IEventBus, EventBus>();
            services.AddSingleton<IEventStore, EventStoreInMemory>();
            //register EventHandlers
            services.AddSingleton<OperationLogEventHandler>();
            services.AddSingleton<NoticeViewEventHandler>();

            // SetDependencyResolver
            DependencyResolver.SetDependencyResolver(services);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory, IEventBus eventBus)
        {
            eventBus.Subscribe<OperationLogEvent, OperationLogEventHandler>(); // 注册操作日志 Event
            eventBus.Subscribe<NoticeViewEvent, NoticeViewEventHandler>(); // 公告

            //LogHelper.LogFactory.AddLog4Net();

            //loggerFactory.AddLog4Net();

            app.UseCustomExceptionHandler();
            app.UseHealthCheck("/health");
            app.UseStaticFiles();
            app.UseSwagger()
                .UseSwaggerUI(c =>
                {
                    // c.RoutePrefix = string.Empty; //
                    c.SwaggerEndpoint($"/swagger/{ApplicationHelper.ApplicationName}/swagger.json", "活动室预约系统 API");
                    c.DocumentTitle = "活动室预约系统 API";
                });

            app.UseRequestLog();
            app.UseCors(builder => builder.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin());

            app.UseAuthentication();
            app.UseMvc(routes =>
            {
                routes.MapRoute("Notice", "/Notice/{path}.html", new
                {
                    controller = "Home",
                    action = "NoticeDetails"
                });

                routes.MapRoute(name: "areaRoute",
                  template: "{area:exists}/{controller=Home}/{action=Index}");

                routes.MapRoute(
                    name: "default",
                    template: "{controller=Home}/{action=Index}");
            });

            // initialize
            app.ApplicationServices.Initialize();
        }
    }
}
