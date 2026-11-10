using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class Loader {

    public enum Scene {
        GameScene,
        MainMenuScene,
        LoadingScene
    }

    private static Scene targetScene;

    //每次切换场景都先进入Loading，而且是双向的
    public static void Load(Scene targetScene) {
        Loader.targetScene = targetScene;

        SceneManager.LoadScene(Scene.LoadingScene.ToString());
    }
    public static void LoaderCallback() {
        SceneManager.LoadScene(targetScene.ToString());
    }

}
