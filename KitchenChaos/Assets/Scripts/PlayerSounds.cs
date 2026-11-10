using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerSounds : MonoBehaviour {

    private Player player;
    private float walkingFootstepTimer;
    private float walkingFootstepTimerMax = .5f;
    private float runningFootstepTimer;
    private float runningFootstepTimerMax = .1f;


    public void Awake() {
        player = GetComponent<Player>();
    }

    public void Update() {
        walkingFootstepTimer -= Time.deltaTime;
        if (walkingFootstepTimer < 0f) {
            walkingFootstepTimer = walkingFootstepTimerMax;

            if (player.IsWalking() && !player.IsRunning()) {
                float volume = 1f;
                SoundManager.Instance.PlayFootstepsSound(player.transform.position, volume);
            }
        }

        runningFootstepTimer -= Time.deltaTime;
        if (runningFootstepTimer < 0f) {
            runningFootstepTimer = runningFootstepTimerMax;

            if (player.IsRunning()) {
                float volume = 1f;
                SoundManager.Instance.PlayFootstepsSound(player.transform.position, volume);
            }
        }
    }
}
