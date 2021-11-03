Functionality that works:
	Construction of the Chord ring modulo 2^160– 
		Each node is assumed to have an IPv6 address which is converted to an identifier value in the range of [0..2^160] 
		using the 160 bit SHA-1 hash value of the IPv6 address. Since the value can be too big to store in an int32 or 
		int64 type variable, a bigint type is used to store the identifier
	Find Successor –
		A node will check for the successor of the id (using successor pointer) or the closest predecessor 
		(in the perspective of the node) of the id using the local finger table. The size of finger table 
		is 160 where finger[i]is successor(finger[i]= n + 2^(i-1) ) and n is the identifier of the node. Due 
		to the nature of finger table, huge number of nodes in the chord circle are skipped while identifying 
		the closest predecessor resulting in a logarithmic(network size) order time complexity for the queries
	Join a Chord ring – 
		A new node can join an existing chord ring by sending messages to the chord node to identify its successor. 
		The stabilization algorithm will kick in to update the predecessors as well as the finger table
	Stabilize – 
		Called periodically (every 50ms) for each node so that its successor pointer can be up to date as well as 
		notify other nodes about its existence in the chord ring. 
	Fix Fingers – 
		Called periodically (every 50ms) for each node so that it’s finger table will be up to date to accommodate 
		snew nodes that joined the chord circle
Results
	Note: We had to wait for stabilization to complete before getting the average hop count. The reason is that a large 
	number of nodes were being added to the network. However, the 	wait is not needed for nodes that join once the chord 
	circle is established given that number of nodes joining are small compared to number of nodes in the circle

			Queries	
		   Avg Hop Count	
Number of Nodes	1	50	100	Avg Hop Count
10		1.6	1.632	1.497	1.576
100		2.25	3.077	3.234	2.853
1000		5.512	4.815	4.76	5.029
5000		5.45	5.885	5.836	5.723
10000		6.04	6.505	6.39	6.311


Largest working network
	The largest network that we managed to simulate comprised of 10000 nodes. We tried with 100000 nodes but however, 
	the stabilization of the network took huge time. The results for these simulations are listed in the above table

