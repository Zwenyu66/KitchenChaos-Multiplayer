using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlatesCounterVisual : MonoBehaviour {

    [SerializeField] private PlatesCounter platesCounter;
    [SerializeField] private Transform counterTopPoint;
    [SerializeField] private Transform plateVisualPrefab;

    private List<GameObject> plateVisualGameObjectList;

    private void Awake() {
        plateVisualGameObjectList = new List<GameObject>();
    }
    // Multiplayer disables single-player event subscriptions, so the host's stock drives this visual directly.
    public void ApplyMultiplayerStock(int stock) {
        if(plateVisualGameObjectList==null)plateVisualGameObjectList=new List<GameObject>();
        stock=Mathf.Clamp(stock,0,4);
        while(plateVisualGameObjectList.Count>stock) {
            int last=plateVisualGameObjectList.Count-1;
            Destroy(plateVisualGameObjectList[last]);plateVisualGameObjectList.RemoveAt(last);
        }
        while(plateVisualGameObjectList.Count<stock) {
            var plate=Instantiate(plateVisualPrefab,counterTopPoint);
            plate.localPosition=new Vector3(0,.1f*plateVisualGameObjectList.Count,0);
            plateVisualGameObjectList.Add(plate.gameObject);
        }
    }


    public void Start() {
        platesCounter.OnPlateSpawned += PlatesCounter_OnPlateSpawned;
        platesCounter.OnPlateRemoved += PlatesCounter_OnPlateRemoved;
    }

    private void PlatesCounter_OnPlateRemoved(object sender, System.EventArgs e) {
        GameObject plateGameObject = plateVisualGameObjectList[plateVisualGameObjectList.Count - 1];
        plateVisualGameObjectList.Remove(plateGameObject);
        Destroy(plateGameObject);
    }

    private void PlatesCounter_OnPlateSpawned(object sender, System.EventArgs e) {
        Transform plateVisualTransform = Instantiate(plateVisualPrefab, counterTopPoint);

        float plateOffSetY = .1f;
        plateVisualTransform.localPosition = new Vector3(0, plateOffSetY * plateVisualGameObjectList.Count, 0);

        plateVisualGameObjectList.Add(plateVisualTransform.gameObject);
    }


}
