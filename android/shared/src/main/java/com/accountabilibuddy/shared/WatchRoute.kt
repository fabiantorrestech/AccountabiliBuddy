package com.accountabilibuddy.shared

/**
 * How the phone may reach the watch (ADR 21), set on the phone before linking and later. Nothing is sent until a watch
 * is linked ([linkedNodeId]). [dataLayerOn]: through the phone over Google's Wearable Data Layer. [relayOn]: let
 * Google's servers pass it along, end-to-end encrypted, when the watch isn't in Bluetooth range; off, it goes only to
 * a watch that's nearby. The watch reaches a PC only through the phone (ADR 45).
 */
data class WatchLinkSettings(
    val linkedNodeId: String? = null,
    val linkedName: String? = null,
    val dataLayerOn: Boolean = true,
    val relayOn: Boolean = true,
)

/** A watch the Data Layer knows: its node id, its name, and whether it's in Bluetooth range of the phone. */
data class WatchNode(val id: String, val name: String, val isNearby: Boolean)

/** Which watches a message may go to under the user's settings. */
object WatchRoute {
    fun choose(settings: WatchLinkSettings, nodes: List<WatchNode>): List<WatchNode> {
        if (!settings.dataLayerOn || settings.linkedNodeId == null) {
            return emptyList()
        }

        val linked = nodes.filter { it.id == settings.linkedNodeId }
        return if (settings.relayOn) linked else linked.filter { it.isNearby }
    }
}
