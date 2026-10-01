using IPA;
using IPA.Config.Stores;
using IPA.Loader;
using SiraUtil.Zenject;
using IPALogger = IPA.Logging.Logger;
using Conf = IPA.Config.Config;
using HarmonyLib;
using System.Reflection;
using System.IO;
using UnityEngine;
using System.Threading.Tasks;
using System;
using Newtonsoft.Json.Linq;
using System.Net.Http;
using System.IO.Compression;
using BookmarkViewer.Installers;
using BookmarkViewer;

namespace BookmarkViewer
{
    [Plugin(RuntimeOptions.DynamicInit), NoEnableDisable]
    public class Plugin
    {
        internal static Assembly Assembly { get; } = Assembly.GetExecutingAssembly();
        private Harmony? _harmony;



        [Init]
        public Plugin(IPALogger logger, Conf conf, Zenjector zenjector)
        {

            Config.Instance = conf.Generated<Config>();

            zenjector.UseLogger(logger);
            zenjector.Install<MenuInstaller>(Location.Menu);

         
        }

        [OnStart]
        public void OnStart()
        {
            _harmony = new Harmony("Pink.BookmarkViewer");
            _harmony.PatchAll(Assembly);
         
        }

        [OnExit]
        public void OnExit()
        {
            Patches.BookmarkPatches.Stop();
            _harmony?.UnpatchSelf();
        }
    }
}
